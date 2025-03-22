//Sprutora.cs

using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;


namespace Shield.Estimator.Shared.Components.EntityFrameworkCore.Sprutora;


[Table("SPR_SPEECH_TABLE")]
public class SprSpeechTable
{
    [Key]
    [Column("S_INCKEY")]
    public long? SInckey { get; set; } = 0;

    [Column("S_TYPE")] //1 - текстовое сообщение, 0 - сеанс связи
    public int? SType { get; set; } = 0;

    [Column("S_PRELOOKED")] //Признак просмотра (0/1)
    public int? SPrelooked { get; set; } = 0;

    [Column("S_DEVICEID")] //Имя устройства регистрации (MEDIUM_R)
    public string? SDeviceid { get; set; } = "MEDIUM_R";

    [Column("S_DURATION")] //durationString = string.Format("+00 {0:D2}:{1:D2}:{2:D2}.000000", duration / 3600, (duration % 3600) / 60, duration % 60);

    /////////////////////////////////////////////////////////////
    //public string? SDuration { get; set; } // Duration - это INTERVAL (в C# - TimeSpan), не string (??? уже не помню, почему string а не TimeSpan)
    public TimeSpan? SDuration { get; set; }//public TimeSpan? Duration { get; set; }

    [Column("S_DATETIME")] //DateTime timestamp = DateTime.ParseExact(timestampString, "dd-MM-yyyy HH:mm:ss", null); || DateTime timestamp = DateTime.Now
    public DateTime? SDatetime { get; set; }

    [Column("S_EVENT")] //Тип события (Событие: -1 -  неизвестно, 0 - назначение трафик-канала, 1 - отключение трафик-канала...)
    public int? SEvent { get; set; } = -1;

    [Column("S_EVENTCODE")] //Событие (оригинал) - GSM (совпадает с RecordType)
    public string? SEventcode { get; set; }

    [Column("S_STANDARD")] //стандарт системы связи - GSM_ABIS
    public string? SStandard { get; set; }

    [Column("S_NETWORK")] //
    public string? SNetwork { get; set; }

    [Column("S_SYSNUMBER3")] //imei
    public string? SSysnumber3 { get; set; }

    [Column("S_SOURCEID")] //??? Номер источника сообщения по базе отбора - 0
    public int? SSourceid { get; set; } = 0;

    [Column("S_STATUS")] //??? статус завершения сеанса - 0
    public int? SStatus { get; set; } = 0;

    [Column("S_BELONG")] //приндалежность - язык оригинала
    public string? SBelong { get; set; }

    [Column("S_SOURCENAME")] //Имя источника - оператор
    public string? SSourcename { get; set; } = "";

    [Column("S_NOTICE")] //примечение
    public string? SNotice { get; set; }

    [Column("S_DCHANNEL")] //номер прямого канала комплекса регистрации (-1, если нет)
    public int? SDchannel { get; set; } = 2; //0 - по описанию

    [Column("S_RCHANNEL")] //номер обратного канала комплекса регистрации (-1, если нет)
    public int? SRchannel { get; set; } = 2; //0 - по описанию

    [Column("S_TALKER")] //пользовательский номер собеседника (тот, кому звонят)
    public string? STalker { get; set; }

    [Column("S_USERNUMBER")] //пользовательский номер источника (тот, кто звонит)
    public string? SUsernumber { get; set; }

    [Column("S_CID")] //идентификатор соты базовой станции
    public string? SCid { get; set; }

    [Column("S_LAC")] //код зоны базовой станции
    public string? SLac { get; set; }

    [Column("S_BASESTATION")] //код зоны базовой станции
    public string? SBasestation { get; set; }

    [Column("S_POSTID")] //имя поста регистрации //LanguageCode
    public string? SPostid { get; set; }


    [Column("S_CALLTYPE")] //тип вызова 0-входящий, 1-исходящий, 2-неизвестный...
    public int? SCalltype { get; set; } = 2;

    [Column("S_SELSTATUS")] //1 - собеседник, 2 - слово в тексте, 3 - геофильтр, 4 - номер в тексте
    public short? SSelstatus { get; set; } // Статус отбора - Наличие признака отбора default = -1

    //без этого столбца Работает Oracle, но не работает Postgres и наоборот
    //modelBuilder.Entity<SprSpeechTable>().Ignore(s => s.SprSpData1Tables);
    public virtual ICollection<SprSpData1Table> SprSpData1Tables { get; set; } = new List<SprSpData1Table>();
}

[Table("SPR_SP_DATA_1_TABLE")]
public class SprSpData1Table
{
    [Key]
    [Column("S_INCKEY")]
    public long? SInckey { get; set; } = 0;

    [Column("S_ORDER")] //Номер записи в сеансе (0 - по умолчанию) - обязательный параметр
    public int? SOrder { get; set; } = 0;

    [Column("S_RECORDTYPE")]//Типзаписи (GSM/SMS Text/UCS2/…) - обязательный параметр
    public string? SRecordtype { get; set; } = "PCMA"; //поиграться с кодировками

    [Column("S_FSPEECH")]
    public byte[]? SFspeech { get; set; }

    [Column("S_RSPEECH")]
    public byte[]? SRspeech { get; set; }

    //без этого столбца Работает Oracle, но не работает Postgres и наоборот
    public virtual SprSpeechTable SInckeyNavigation { get; set; } = null!;
}

[Table("SPR_SP_COMMENT_TABLE")]
public class SprSpCommentTable
{
    [Key]
    [Column("S_INCKEY")]
    public long? SInckey { get; set; } = 0;

    [Column("S_COMMENT")]
    public byte[]? SComment { get; set; }
    public virtual SprSpeechTable SInckeyNavigation { get; set; } = null!;

}
/*

//Более компактная запись, когда название таблицы совпадает с названием класса
//и название поля совпадает с названием свойства
public class SPR_SP_COMMENT_TABLE  // Без [Table("SPR_SP_COMMENT_TABLE")]
{
    [Key] // Указывает, что это первичный ключ
    [Column("S_INCKEY")] // Указывает, что свойство связано с колонкой S_INCKEY
    public long Id { get; set; } = 0; // Поле S_INCKEY
    public byte[]? S_COMMENT { get; set; } // Поле S_COMMENT, без [Column("S_COMMENT")]
}
*/
