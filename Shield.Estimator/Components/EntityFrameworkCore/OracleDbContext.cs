//OracleDbContext.cs

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MudBlazor.Extensions;
using Shield.Estimator.Shared.Components.EntityFrameworkCore.Sprutora;

namespace Shield.Estimator.Shared.Components.EntityFrameworkCore;


// Определение контекста базы данных для работы с Entity Framework Core
public static class DoubleExtensions
{
    public static bool IsInteger(this double value)
    {
        return Math.Abs(value % 1) <= double.Epsilon * 100;
    }
}


public class OracleDbContext : BaseDbContext
{
    public OracleDbContext(DbContextOptions<OracleDbContext> options/*, string scheme = null*/) : base(options)
    {
    }
    public override DbSet<SprSpeechTable> SprSpeechTables { get; set; }
    public override DbSet<SprSpData1Table> SprSpData1Tables { get; set; }
    public override DbSet<SprSpCommentTable> SprSpCommentTables { get; set; }

    // Переопределение метода для настройки моделей (сопоставления сущностей с таблицами)
    /*
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {

        // Указываем, что сущность SPR_SPEECH_TABLE сопоставляется с таблицей "SPR_SPEECH_TABLE"
        modelBuilder.Entity<SprSpeechTable>().ToTable("SPR_SPEECH_TABLE");

        // так работало с чтением double в БД
        modelBuilder.Entity<SprSpeechTable>()
            .Property(e => e.SInckey)
            .HasConversion<double?>(
            v => v.HasValue ? (double?)v.Value : null,
            v => v.HasValue ? (long?)Convert.ToInt64(v.Value) : null);

        

        // Настраиваем свойства сущности SPR_SP_DATA_1_TABLE для полей Fspeech и Rspeech, указывая их тип как BLOB
        modelBuilder.Entity<SprSpData1Table>()
            .ToTable("SPR_SP_DATA_1_TABLE")  // Указываем имя таблицы
            .Property(b => b.SFspeech)       // Указываем свойство Fspeech
            .HasColumnType("BLOB");         // Устанавливаем тип данных для колонки как BLOB

        modelBuilder.Entity<SprSpData1Table>()
            .Property(b => b.SRspeech)       // Указываем свойство Rspeech
            .HasColumnType("BLOB");         // Устанавливаем тип данных для колонки как BLOB

        // Сопоставляем сущность SPR_SP_COMMENT_TABLE с таблицей "SPR_SP_COMMENT_TABLE"
        modelBuilder.Entity<SprSpCommentTable>().ToTable("SPR_SP_COMMENT_TABLE")
        .Property(b=>b.SComment).HasColumnType("BLOB");

        modelBuilder.Entity<SprSpeechTable>().Ignore(s => s.SprSpData1Tables);
        modelBuilder.Entity<SprSpData1Table>().Ignore(s => s.SInckeyNavigation);
        modelBuilder.Entity<SprSpCommentTable>().Ignore(s => s.SInckeyNavigation);
        
        // Вызов базовой реализации метода
        base.OnModelCreating(modelBuilder);
    }
    */
    // Метод для конвертации double? -> long? с проверками
    private static long? ConvertDoubleToLong(double? value)
    {
        if (!value.HasValue) return null;

        try
        {
            /*
            if (Math.Abs(value % 1) > double.Epsilon * 100)
                throw new InvalidOperationException("Non-integer value detected");
            */
            return checked((long)Math.Round(value.Value, 0));
        }
        catch (OverflowException)
        {
            // Логирование ошибки
            Console.WriteLine($"Overflow: {value}");
            return null;
        } 
        /*
        double v = value.Value;

        // Проверка на целочисленность
        if (v % 1 != 0)
            throw new InvalidCastException($"S_INCKEY содержит дробное значение: {v}");

        // Проверка на переполнение
        if (v < long.MinValue || v > long.MaxValue)
            throw new InvalidCastException($"S_INCKEY выходит за пределы long: {v}");

        return (long)v;
        */
    }



    // Универсальная конфигурация для SInckey
    void ConfigureInckey<TEntity>(EntityTypeBuilder<TEntity> builder) where TEntity : class
    {
        builder.Property<long?>(nameof(SprSpeechTable.SInckey))
            .HasColumnType("number(20)") // Базовый тип для Oracle
            .HasConversion(
                v => v.HasValue ? (double?)Convert.ToDouble(v.Value) : null,
                v => v.HasValue ? ConvertDoubleToLong(v.Value) : null
            );
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Конфигурация для SprSpeechTable
        modelBuilder.Entity<SprSpeechTable>(entity =>
        {
            entity.ToTable("SPR_SPEECH_TABLE");
            
            ConfigureInckey(entity);
            /*
            entity.Property(e => e.SInckey)
                .HasConversion(
                    v => v.HasValue ? (double?)v.Value : null,       // Преобразование при записи (C# -> Oracle)
                    v => ConvertDoubleToLong(v)                      // Преобразование при чтении (Oracle -> C#)
                )
                //.HasColumnType("BINARY_DOUBLE")
                .ValueGeneratedNever(); // Отключаем автоматическую генерацию ключа
            */
        });

        // Конфигурация для SprSpData1Table
        modelBuilder.Entity<SprSpData1Table>(entity =>
        {
            entity.ToTable("SPR_SP_DATA_1_TABLE");
            /*
            entity.Property(e => e.SInckey)
                .HasConversion(
                    v => v.HasValue ? (double?)v.Value : null,
                    v => ConvertDoubleToLong(v)
                )
                //.HasColumnType("BINARY_DOUBLE")
                .ValueGeneratedNever(); // Отключаем автоматическую генерацию ключа
            */
            entity.Property(e => e.SFspeech).HasColumnName("S_FSPEECH");
            entity.Property(e => e.SRspeech).HasColumnName("S_RSPEECH");
            entity.Property(e => e.SOrder).HasColumnName("S_ORDER");
            entity.Property(e => e.SRecordtype)
                .HasMaxLength(30)
                .HasColumnName("S_RECORDTYPE");


            entity.Property(b => b.SFspeech).HasColumnType("BLOB");
            entity.Property(b => b.SRspeech).HasColumnType("BLOB");
        });

        // Конфигурация для SprSpCommentTable
        modelBuilder.Entity<SprSpCommentTable>(entity =>
        {
            /*
            entity.ToTable("SPR_SP_COMMENT_TABLE");
            entity.Property(e => e.SInckey)
                .HasConversion(
                    v => v.HasValue ? (double?)v.Value : null,
                    v => ConvertDoubleToLong(v)
                )
                //.HasColumnType("BINARY_DOUBLE")
                .ValueGeneratedNever(); // Отключаем автоматическую генерацию ключа
            */
            entity.Property(b => b.SComment).HasColumnType("BLOB");
        });

        // Отключение навигационных свойств
        modelBuilder.Entity<SprSpeechTable>().Ignore(s => s.SprSpData1Tables);
        modelBuilder.Entity<SprSpData1Table>().Ignore(s => s.SInckeyNavigation);
        modelBuilder.Entity<SprSpCommentTable>().Ignore(s => s.SInckeyNavigation);

        base.OnModelCreating(modelBuilder);
    }
}