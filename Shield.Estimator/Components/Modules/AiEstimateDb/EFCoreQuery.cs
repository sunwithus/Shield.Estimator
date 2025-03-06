//EFCoreQuery.cs

using Microsoft.EntityFrameworkCore;
using Shield.Estimator.Shared.Components.EntityFrameworkCore.Sprutora;
using Shield.Estimator.Shared.Components.EntityFrameworkCore;
using System.Text;
using System.Data;
using Org.BouncyCastle.Crypto.Digests;
using DocumentFormat.OpenXml.Office2010.Excel;
using FFMpegCore.Enums;


namespace Shield.Estimator.Shared.Components.Modules.AiEstimateDb
{
    public class EFCoreQuery
    {
        public static async Task<List<SprSpeechTable>> GetSpeechRecords(
            DateTime StartDateTime, 
            DateTime EndDateTime, 
            int TimeInterval,
            BaseDbContext context,
            List<string> _ignoreRecordType)
        {
            return await context.SprSpeechTables
               .Where(x => x.SDatetime >= StartDateTime && x.SDatetime <= EndDateTime
               && x.SType == 0 // Тип записи (-1 – неизвестно,0 – сеанс связи, 1 – сообщение, 2 – биллинг,3 – служебное сообщение, 4 – регистрация автотранспорта
               && x.SDuration > TimeSpan.FromSeconds(TimeInterval)
               && (x.SNotice == null || x.SNotice == "")
               && !_ignoreRecordType.Contains(x.SEventcode))
               .OrderByDescending(x => x.SDatetime)
               //.AsEnumerable() // Evaluate the query so far on the database
               //.Where(x => EFCoreQuery.ConvertDurationStringToSeconds(x.SDuration) > EFCoreQuery.ConvertDurationStringToSeconds(TimeInterval))
               .ToListAsync();
        }

        public static async Task<(byte[]?, byte[]?, string?, string?)> GetAudioDataAsync(long? key, BaseDbContext db)
        {
            var results = await db.SprSpData1Tables.Where(x => x.SInckey == key).Select(x => new
            {
                AudioDataLeft = x.SFspeech,
                AudioDataRight = x.SRspeech,
                RecordType = x.SRecordtype
            }).ToListAsync();

            var result = results.FirstOrDefault();

            var results2 = await db.SprSpeechTables.Where(x => x.SInckey == key).Select(x => new
            {
                Eventcode = x.SEventcode
            }).ToListAsync();

            var result2 = results2.FirstOrDefault();

            return (result?.AudioDataLeft, result?.AudioDataRight, result?.RecordType, result2?.Eventcode);
        }

        public static async Task<string> GetCommentDataAsync(long? key, BaseDbContext db)
        {
            var results = await db.SprSpCommentTables.Where(x => x.SInckey == key).Select(x => x.SComment).ToListAsync();
            byte[]? result = results.FirstOrDefault();
            return result != null ? Encoding.GetEncoding("windows-1251").GetString(result) : "Комментарий отсутствует.";
        }

        public static async Task UpdateLangInfo(long? key, string detectedLanguage, string langCode, BaseDbContext db)
        {
            if (key == null)
            {
                return; // Если запись с таким ключом не найдена, завершаем метод
            }
            Console.WriteLine($"{key}");
            SprSpeechTable speech = db.SprSpeechTables.Where(c => c.SInckey == key).ToList().FirstOrDefault();
            speech.SBelong = detectedLanguage;
            speech.SPostid = langCode;
            Console.WriteLine($"langCode => {detectedLanguage}");
            try
            {
                Console.WriteLine($"3");
                db.Entry(speech).State = EntityState.Modified;
                await db.SaveChangesAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Обработка исключений, например, если записи с таким ключом больше нет
                throw new Exception($"Ошибка при обновлении данных UpdateLangInfo для ключа {key}: {ex.Message}");
            }
        }

        public static async Task InsertOrUpdateCommentAsync(
            long? key,
            string text,
            string detectedLanguage,
            string responseOllamaText,
            string langCode,
            BaseDbContext db,
            int backLight)
        {
            // Register the encoding provider
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var sb = new StringBuilder();
            sb.Append(responseOllamaText);
            sb.Append("\n##############################\n");
            sb.Append(text);

            // т.к. в БД доступны только кирилица и латиница, поэтому обязательно текст будем переводить
            byte[] commentBytes = Encoding.GetEncoding(1251).GetBytes(sb.ToString());

            string dangerLevelString = int.TryParse(responseOllamaText.Substring(0, 1), out int dangerLevel) ? dangerLevel.ToString() : "unknown";

            //Selstatus //1 - собеседник, 2 - слово в тексте, 3 - геофильтр, 4 - номер в тексте
            short selStatus = (dangerLevel > 0 && dangerLevel - backLight >= 0) ? (short)4 : (short)-1;

            try
            {
                // Проверка существования и обновление/добавление записи в SPR_SP_COMMENT_TABLE
                // Использование AsEnumerable() приводит к выполнению запроса и загрузке данных в память, а затем LastOrDefault() выполняется уже в памяти.
                // AsEnumerable() - обязательно, иначе ошибка Oracle 11.2 (т.к. EFCore использует новый синтаксис SQL)
                //var comment = db.SprSpCommentTables.Where(c => c.SInckey == key).AsEnumerable().FirstOrDefault();
                //var comment = await db.SprSpCommentTables.FindAsync(key); //так не работает
                var comment = db.SprSpCommentTables.Where(c => c.SInckey == key).ToList().FirstOrDefault();
                if (comment == null)
                {
                    comment = new SprSpCommentTable
                    {
                        SInckey = key,
                        SComment = commentBytes
                    };
                    await db.SprSpCommentTables.AddAsync(comment);
                }
                else
                {
                    comment.SComment = commentBytes;
                    db.Entry(comment).State = EntityState.Modified;
                }

                // Проверка существования и обновление/добавление записи в SPR_SPEECH_TABLE
                var speech = db.SprSpeechTables.Where(c => c.SInckey == key).ToList().FirstOrDefault();
                //var speech = await db.SprSpeechTables.FindAsync(key); //так не работает
                if (speech == null)
                {
                    speech = new SprSpeechTable
                    {
                        SInckey = key,
                        SBelong = detectedLanguage,
                        SPostid = langCode,
                        SNotice = dangerLevelString,
                        SSelstatus = selStatus
                    };
                    await db.SprSpeechTables.AddAsync(speech);
                }
                else
                {
                    speech.SBelong = detectedLanguage;
                    speech.SNotice = dangerLevelString;
                    speech.SPostid = langCode;
                    speech.SSelstatus = selStatus;
                    db.Entry(speech).State = EntityState.Modified;
                }

                await db.SaveChangesAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ConsoleCol.WriteLine($"InsertOrUpdateCommentAsync  => {ex.Message}", ConsoleColor.Red);
                throw;
            }
        }

        public static async Task UpdateManyNoticeValuesAsync(List<long?> keys, BaseDbContext db, string? value = null)
        {
            await db.SprSpeechTables
                .Where(s => keys.Contains(s.SInckey))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.SNotice, value));
        }
        public static async Task UpdateNoticeValueAsync(long? key, BaseDbContext db, string? value = null)
        {
            try
            {
                var item = await db.SprSpeechTables.Where(c => c.SInckey == key).ToListAsync();
                SprSpeechTable speech = item.FirstOrDefault();
                //в Oracle 11.2 так не работает => SprSpeechTable speech = await db.SprSpeechTables.FindAsync(key);
                if (speech != null)
                {
                    speech.SNotice = value;
                    db.Entry(speech).State = EntityState.Modified;
                    await db.SaveChangesAsync().ConfigureAwait(false);
                }
                
            }
            catch (Exception ex)
            {
                ConsoleCol.WriteLine("Ошибка в InsertNullToNoticeAsync => " + ex.Message, ConsoleColor.Red);
            }
        }
        public static async Task<List<long?>> GetSInckeyRecordsForNoticeNull(DateTime StartDateTime, DateTime EndDateTime, string SourceName, BaseDbContext db)
        {
            if (SourceName == "*")
            {
                return await db.SprSpeechTables
                    .Where(x => x.SDatetime >= StartDateTime && x.SDatetime <= EndDateTime
                    && (x.SNotice != null || x.SNotice != ""))
                    .Select(x => x.SInckey)
                    .ToListAsync();
            }
            return await db.SprSpeechTables
                .Where(x => x.SDatetime >= StartDateTime && x.SDatetime <= EndDateTime && x.SSourcename == SourceName
                && (x.SNotice != null || x.SNotice != ""))
                .Select(x => x.SInckey)
                .ToListAsync();
        }

        public static async Task<List<long?>> GetSInckeyRecordsPostworks(DateTime StartDateTime, DateTime EndDateTime, int Duration, bool Prelooked, List<string> _ignoreRecordType, BaseDbContext db)
        {
            return await db.SprSpeechTables
                .Where(x => x.SDatetime >= StartDateTime && x.SDatetime <= EndDateTime
                && x.SType == 0
                && (Prelooked ? x.SPrelooked >= 0 : x.SPrelooked < 1)
                && x.SDuration >= TimeSpan.FromSeconds(Duration)
                && !_ignoreRecordType.Contains(x.SEventcode))
                .Select(x => x.SInckey)
                .ToListAsync();
        }

        public static async Task<List<SprSpeechTable>> GetSpeechRecordsById(List<long?> Ids, BaseDbContext context)
        {
            return await context.SprSpeechTables
               .Where(x => Ids.Contains(x.SInckey)) // Тип записи 0 – сеанс связи
               .OrderByDescending(x => x.SDatetime)
               .ToListAsync();
        }

        public static async Task SetPrelookedById(long? Id, BaseDbContext context)
        {
            try
            {
                var item = await context.SprSpeechTables.Where(c => c.SInckey == Id).ToListAsync();
                SprSpeechTable record = item.FirstOrDefault();
                //в Oracle 11.2 так не работает => SprSpeechTable record = await context.SprSpeechTables.FindAsync(Id);

                Console.WriteLine("record.id = " + record.SInckey);
                Console.WriteLine("Id = " + Id);
                if (record != null)
                {
                    record.SPrelooked = 1;
                    context.Entry(record).State = EntityState.Modified;
                    await context.SaveChangesAsync().ConfigureAwait(false); // Use ConfigureAwait(false) to avoid deadlocks
                }
            }
            catch (Exception ex)
            {
                ConsoleCol.WriteLine("Ошибка в SetPrelookedById => " + ex.Message, ConsoleColor.Red);
            }
        }

    }
}

