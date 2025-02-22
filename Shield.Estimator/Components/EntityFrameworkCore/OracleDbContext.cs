//OracleDbContext.cs

using Microsoft.EntityFrameworkCore;
using Shield.Estimator.Shared.Components.EntityFrameworkCore.Sprutora;

namespace Shield.Estimator.Shared.Components.EntityFrameworkCore
{

    // Определение контекста базы данных для работы с Entity Framework Core
    public class OracleDbContext : BaseDbContext
    {
        public OracleDbContext(DbContextOptions<OracleDbContext> options/*, string scheme = null*/) : base(options)
        {
        }
        public override DbSet<SprSpeechTable> SprSpeechTables { get; set; }
        public override DbSet<SprSpData1Table> SprSpData1Tables { get; set; }
        public override DbSet<SprSpCommentTable> SprSpCommentTables { get; set; }

        // Переопределение метода для настройки моделей (сопоставления сущностей с таблицами)
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {

            // Указываем, что сущность SPR_SPEECH_TABLE сопоставляется с таблицей "SPR_SPEECH_TABLE"
            modelBuilder.Entity<SprSpeechTable>().ToTable("SPR_SPEECH_TABLE");

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
    }
}

