//PostgresDbContext.cs

using Microsoft.EntityFrameworkCore;
using Shield.Estimator.Shared.Components.EntityFrameworkCore.Sprutora;

namespace Shield.Estimator.Shared.Components.EntityFrameworkCore
{

public partial class PostgresDbContext : BaseDbContext
{
    public PostgresDbContext(DbContextOptions<PostgresDbContext> options)
        : base(options)
    {
    }
    public override DbSet<SprSpeechTable> SprSpeechTables { get; set; }
    public override DbSet<SprSpData1Table> SprSpData1Tables { get; set; }
    public override DbSet<SprSpCommentTable> SprSpCommentTables { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SprSpCommentTable>(entity =>
        {
            entity.HasKey(e => e.SInckey); // Define SInckey as the primary key
            //entity.HasNoKey();
            entity.ToTable("spr_sp_comment_table", "sprut");
            
            entity.HasIndex(e => e.SInckey, "foreign16");

            entity.HasIndex(e => e.SInckey, "spr_sp_comment_table_s_inckey_key").IsUnique();

            entity.Property(e => e.SComment).HasColumnName("s_comment");
            entity.Property(e => e.SInckey).HasColumnName("s_inckey");
            ////////////////////////////////////////////////////////////////
            entity.HasOne(d => d.SInckeyNavigation).WithOne()
                  .HasForeignKey<SprSpCommentTable>(d => d.SInckey)
                  .OnDelete(DeleteBehavior.ClientSetNull) // or other appropriate delete behavior
                  .HasConstraintName("spcm_inckey");
            /**/
        });

        modelBuilder.Entity<SprSpData1Table>(entity =>
        {

            entity.ToTable("spr_sp_data_1_table", "sprut");

            
            entity.HasIndex(e => e.SInckey, "foreign17");
            
            entity.HasIndex(e => e.SRecordtype, "spdata_rectype");

            entity.HasIndex(e => new { e.SInckey, e.SOrder }, "spdt1_incorder").IsUnique();

            entity.Property(e => e.SFspeech).HasColumnName("s_fspeech");
            entity.Property(e => e.SInckey).HasColumnName("s_inckey");
            entity.Property(e => e.SOrder).HasColumnName("s_order");

            entity.Property(e => e.SRecordtype)
                .HasMaxLength(30)
                .HasColumnName("s_recordtype");
            entity.Property(e => e.SRspeech).HasColumnName("s_rspeech");
            ///////////////////////////////////////////////////////////////////////////
            entity.HasOne(d => d.SInckeyNavigation)
                  .WithMany(p => p.SprSpData1Tables)
                  .HasForeignKey(d => d.SInckey)
                  .OnDelete(DeleteBehavior.ClientSetNull) // or other appropriate delete behavior
                  .HasConstraintName("spdt1_inckey");
        });


        modelBuilder.Entity<SprSpeechTable>(entity =>
        {


            entity.ToTable("spr_speech_table", "sprut");

            entity.Property(e => e.SDatetime).HasConversion(
    v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : DateTime.MinValue,
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

            
            entity.HasKey(e => e.SInckey).HasName("speech_key");

            entity.HasIndex(e => e.SInckey, "primary13").IsUnique();
            
            entity.HasIndex(e => e.SBasestation, "speech_basestation");

            entity.HasIndex(e => e.SCid, "speech_cid");

            entity.HasIndex(e => e.SDatetime, "speech_datetime");

            entity.HasIndex(e => e.SEventcode, "speech_eventcode");

            entity.HasIndex(e => e.SLac, "speech_lac");

            entity.HasIndex(e => new { e.SLac, e.SCid }, "speech_lac_cid");

            entity.HasIndex(e => e.SNotice, "speech_notice");

            entity.HasIndex(e => e.SSourcename, "speech_sourcename");

            entity.HasIndex(e => e.SSysnumber3, "speech_sysnumber3");

            entity.HasIndex(e => e.STalker, "speech_talker");

            entity.HasIndex(e => e.SUsernumber, "speech_usernumber");


            entity.Property(e => e.SInckey)
                .ValueGeneratedNever()
                .HasColumnName("s_inckey");
            entity.Property(e => e.SBasestation)
                .HasMaxLength(250)
                .HasColumnName("s_basestation");
            entity.Property(e => e.SBelong)
                .HasMaxLength(50)
                .HasColumnName("s_belong");
            entity.Property(e => e.SCalltype).HasColumnName("s_calltype");
            entity.Property(e => e.SCid)
                .HasMaxLength(30)
                .HasColumnName("s_cid");
            entity.Property(e => e.SDatetime).HasColumnName("s_datetime");
            entity.Property(e => e.SDchannel).HasColumnName("s_dchannel");
            entity.Property(e => e.SDeviceid)
                .HasMaxLength(20)
                .HasColumnName("s_deviceid");
            entity.Property(e => e.SDuration).HasColumnName("s_duration");
            entity.Property(e => e.SEvent).HasColumnName("s_event");
            entity.Property(e => e.SSelstatus).HasColumnName("s_selstatus");
            entity.Property(e => e.SEventcode)
                .HasMaxLength(30)
                .HasColumnName("s_eventcode");
            entity.Property(e => e.SLac)
                .HasMaxLength(30)
                .HasColumnName("s_lac");
            entity.Property(e => e.SNetwork)
                .HasMaxLength(50)
                .HasColumnName("s_network");
            entity.Property(e => e.SNotice)
                .HasMaxLength(100)
                .HasColumnName("s_notice");
            entity.Property(e => e.SPostid)
                .HasMaxLength(20)
                .HasColumnName("s_postid");
            entity.Property(e => e.SPrelooked).HasColumnName("s_prelooked");
            entity.Property(e => e.SRchannel).HasColumnName("s_rchannel");
            entity.Property(e => e.SSourceid).HasColumnName("s_sourceid");
            entity.Property(e => e.SSourcename)
                .HasMaxLength(250)
                .HasColumnName("s_sourcename");
            entity.Property(e => e.SStandard)
                .HasMaxLength(20)
                .HasColumnName("s_standard");
            entity.Property(e => e.SStatus).HasColumnName("s_status");
            entity.Property(e => e.SSysnumber3)
                .HasMaxLength(20)
                .HasColumnName("s_sysnumber3");
            entity.Property(e => e.STalker)
                .HasMaxLength(40)
                .HasColumnName("s_talker");
            entity.Property(e => e.SType).HasColumnName("s_type");
            entity.Property(e => e.SUsernumber)
                .HasMaxLength(40)
                .HasColumnName("s_usernumber");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}

}
