//SpeechDataService.cs
using Microsoft.EntityFrameworkCore;
using Shield.Estimator.Shared.Components.EntityFrameworkCore;
using Shield.Estimator.Shared.Components.Modules._Shared;

namespace Shield.Estimator.Shared.Components.Modules.MakingWord
{
    public class DbQuery
    {
        public static async Task<List<SpeechData>> GetSpeechDataByIdAsync(long? id, BaseDbContext context)
        {
            try
            {
                var speechDataList = await context.SprSpeechTables.Where(x => x.SInckey == id).ToListAsync();
                
                if (speechDataList == null || !speechDataList.Any() || speechDataList?.FirstOrDefault()?.SInckey != id)
                {
                    await context.Database.CloseConnectionAsync();
                    return null;
                }

                var commentTables = await context.SprSpCommentTables.Where(x => x.SInckey == id).ToListAsync();
                var data1Tables = await context.SprSpData1Tables.Where(x => x.SInckey == id).ToListAsync();
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
                    RecordType = speech.SEventcode,
                    Comment = commentTables.FirstOrDefault(c => c.SInckey == speech.SInckey)?.SComment,
                    AudioF = data1Tables.FirstOrDefault(c => c.SInckey == speech.SInckey)?.SFspeech,
                    AudioR = data1Tables.FirstOrDefault(c => c.SInckey == speech.SInckey)?.SRspeech,
                    //RecordType = data1Tables.FirstOrDefault(c => c.SInckey == speech.SInckey)?.SRecordtype
                }).ToList();

                await context.Database.CloseConnectionAsync();
                return speechData;
            }
            catch(Exception ex)
            {
                Console.WriteLine("Error: " + ex);
                await context.Database.CloseConnectionAsync();
                return null;
            }

        }
    }
}