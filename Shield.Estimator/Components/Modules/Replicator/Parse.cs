//OraSettings.cs
using FFMpegCore.Pipes;
using FFMpegCore;
using System.Diagnostics;
using Microsoft.VisualBasic;

namespace Shield.Estimator.Shared.Components.Modules.Replicator
{
    public class Parse
    {
        private const string DateFormatWav = "dd-MM-yyyy HH:mm:ss";
        private const string DateFormatMp3 = "yyyy-MM-dd HH:mm:ss";

        public class ParsedIdenties
        {
            public DateTime Timestamp { get; set; } = DateTime.Now;
            public string IMEI { get; set; } = "";
            public string Caller { get; set; } = "";
            public string Talker { get; set; } = "";
            public short Calltype { get; set; } = 2; // Calltype = 2 - неизвестно, 0 - входящий, 1 - исходящий
        }

        public static ParsedIdenties FormFileName(string filePath)
        {
            var fileExt = Path.GetExtension(filePath);
            var fileNameNoExt = Path.GetFileNameWithoutExtension(filePath);
            var parts = fileNameNoExt.Split('_');
            try
            {
                //01012016_000759_35000000000000_79046283999_79046283999.wav
                if (parts.Length == 5)
                {
                    string timestampString = parts[0].Insert(2, "-").Insert(5, "-") + " " + parts[1].Insert(2, ":").Insert(5, ":");
                    DateTime timestamp = DateTime.ParseExact(timestampString, DateFormatWav, null);
                    Console.WriteLine(timestamp.ToString());

                    return new ParsedIdenties
                    {
                        Timestamp = timestamp,
                        IMEI = parts[2],
                        Caller = parts[3],
                        Talker = parts[4],
                        Calltype = 2
                    };
                }
                //79841944120_79242505061_Call_In_2023-11-23_16_15_36.mp3
                else if (parts.Length == 8)
                {
                    string timestampString = parts[4] + " " + parts[5].Substring(0, 2) + ":" + parts[6].Substring(0, 2) + ":" + parts[7].Substring(0, 2);
                    DateTime timestamp = DateTime.ParseExact(timestampString, DateFormatMp3, null);
                    Console.WriteLine(timestamp.ToString());

                    short calltype = (short)((parts[3] == "In") ? 0 : (parts[3] == "Out") ? 1 : 2); //тип вызова 0-входящий, 1-исходящий, 2-неизвестный...
                    return new ParsedIdenties
                    {
                        Timestamp = timestamp,
                        IMEI = "",
                        Caller = parts[0],
                        Talker = parts[1],
                        Calltype = calltype
                    };
                }
                else
                {
                    throw new InvalidOperationException("Идентификаторы не получены " + filePath);
                }
            }
            catch (FormatException ex)
            {
                Console.WriteLine("Не удалось получить данные из названия файла: " + fileNameNoExt + ". Ошибка: " + ex.Message);
                return new ParsedIdenties();
            }
            catch (Exception ex)
            {
                Console.WriteLine("ParsedIdenties Exception: " + ex.Message);
                return new ParsedIdenties();
            }
        }
    }



}