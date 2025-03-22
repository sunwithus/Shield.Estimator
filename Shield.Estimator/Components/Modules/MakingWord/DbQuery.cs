//DbQuery.cs
using Microsoft.EntityFrameworkCore;
using Shield.Estimator.Shared.Components.EntityFrameworkCore;
using Shield.Estimator.Shared.Components.Modules._Shared;

namespace Shield.Estimator.Shared.Components.Modules.MakingWord;

public class DbQuery
{
    public static async Task<List<SpeechData>> GetSpeechDataByIdAsync(long? id, BaseDbContext context)
    {
        try
        {
            context.Database.SetCommandTimeout(60);
            Console.WriteLine("SInckey => " + id);
            var speechDataList = await context.SprSpeechTables.Where(x => x.SInckey == id).AsNoTracking().ToListAsync();
            /*
            var speechDataList = await context.SprSpeechTables
                .Include(x => x.SprSpCommentTables)
                .Include(x => x.SprSpData1Tables)
                .Where(x => x.SInckey == id)
                .AsNoTracking() // Если данные не нужно изменять
                .ToListAsync();
            */

            if (!speechDataList.Any())
            {
                //await context.Database.CloseConnectionAsync();
                Console.WriteLine("GetSpeechDataByIdAsync => speechDataList problems : данные не найдены.");
                return null;
            }

            var commentTables = await context.SprSpCommentTables.Where(x => x.SInckey == id).ToListAsync();
            var data1Tables = await context.SprSpData1Tables.Where(x => x.SInckey == id).ToListAsync();
            /*
            var data1TableDict = (await context.SprSpData1Tables
                .Where(x => x.SInckey == id)
                .ToListAsync())
                .ToLookup(x => x.SInckey);

            var commentDict = (await context.SprSpCommentTables
                .Where(x => x.SInckey == id)
                .ToListAsync())
                .ToLookup(x => x.SInckey);
            */
            var speechData = speechDataList?.Select(speech => new SpeechData
            {
                Id = speech.SInckey,
                Deviceid = speech.SDeviceid,
                Duration = speech.SDuration,
                Datetime = speech.SDatetime,
                Belong = speech.SBelong,
                Sourcename = speech.SSourcename,
                Talker = speech.STalker,
                Usernumber = speech.SUsernumber,
                Calltype = speech.SCalltype,
                Cid = speech.SCid,
                Lac = speech.SLac,
                Basestation = speech.SBasestation,
                EventCode = speech.SEventcode,
                Comment = commentTables.FirstOrDefault(c => c.SInckey == speech.SInckey)?.SComment,
                AudioF = data1Tables.FirstOrDefault(c => c.SInckey == speech.SInckey)?.SFspeech,
                AudioR = data1Tables.FirstOrDefault(c => c.SInckey == speech.SInckey)?.SRspeech,
                RecordType = data1Tables.FirstOrDefault(c => c.SInckey == speech.SInckey)?.SRecordtype
                /*
                    // ... остальные свойства ...
                    Comment = commentDict[speech.SInckey].FirstOrDefault()?.SComment,
                    AudioF = data1TableDict[speech.SInckey].FirstOrDefault()?.SFspeech,
                    AudioR = data1TableDict[speech.SInckey].FirstOrDefault()?.SRspeech
                */
            }).ToList();

            await context.Database.CloseConnectionAsync();
            return speechData;
        }
        catch(Exception ex)
        {
            Console.WriteLine("Error: " + ex);
            return null;
        }

    }
}