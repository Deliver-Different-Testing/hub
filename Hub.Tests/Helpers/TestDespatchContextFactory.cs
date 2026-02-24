using Hub.Models;
using Microsoft.EntityFrameworkCore;

namespace Hub.Tests.Helpers;

public static class TestDespatchContextFactory
{
    public static DynamicDespatchDbContext Create(string? dbName = null)
    {
        dbName ??= Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<DespatchContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var connectionStringManager = new ConnectionStringManager();
        connectionStringManager.SetConnectionString("Server=fake;Database=fake;");

        var context = new DynamicDespatchDbContext(options, connectionStringManager);
        context.Database.EnsureCreated();
        return context;
    }

    public static DynamicDespatchDbContext CreateWithSeedData(string? dbName = null)
    {
        var context = Create(dbName);
        SeedData(context);
        return context;
    }

    private static void SeedData(DynamicDespatchDbContext context)
    {
        var client = new TucClient
        {
            UcclId = 1,
            UcclName = "Test Client",
            UcclLegalName = "Test Client Ltd",
            UcclCode = "TC001",
            UcclInternal = false,
            UcclActive = true,
            UcclGroupId = 0,
            Smsname = "TestSMS",
            Created = DateTime.Now,
            CreatedBy = "test",
            LastModified = DateTime.Now,
            LastModifiedBy = "test"
        };

        var subClient = new TucClient
        {
            UcclId = 2,
            UcclName = "Sub Client",
            UcclLegalName = "Sub Client Ltd",
            UcclCode = "SC001",
            UcclInternal = false,
            UcclActive = true,
            UcclGroupId = 1,
            Smsname = "SubSMS",
            Created = DateTime.Now,
            CreatedBy = "test",
            LastModified = DateTime.Now,
            LastModifiedBy = "test"
        };

        context.TucClients.AddRange(client, subClient);

        var activeContact = new TucClientContact
        {
            UcctId = 1,
            UcctClientId = 1,
            UcctFirstname = "John",
            UcctSurname = "Doe",
            UcctEmail = "john@test.com",
            UserName = "john@test.com",
            Active = true,
            HasEmail = true,
            ValidatedEmail = true,
            StaffId = 10,
            Created = DateTime.Now,
            CreatedBy = "test",
            LastModified = DateTime.Now,
            LastModifiedBy = "test"
        };

        var inactiveContact = new TucClientContact
        {
            UcctId = 2,
            UcctClientId = 1,
            UcctFirstname = "Jane",
            UcctSurname = "Doe",
            UcctEmail = "jane@test.com",
            UserName = "jane@test.com",
            Active = false,
            HasEmail = true,
            ValidatedEmail = true,
            Created = DateTime.Now,
            CreatedBy = "test",
            LastModified = DateTime.Now,
            LastModifiedBy = "test"
        };

        var staffContact = new TucClientContact
        {
            UcctId = 3,
            UcctClientId = 1,
            UcctFirstname = "Staff",
            UcctSurname = "User",
            UcctEmail = "staff@test.com",
            UserName = "staff@test.com",
            Active = true,
            HasEmail = true,
            ValidatedEmail = true,
            StaffId = 11,
            Created = DateTime.Now,
            CreatedBy = "test",
            LastModified = DateTime.Now,
            LastModifiedBy = "test"
        };

        var legacyContact = new TucClientContact
        {
            UcctId = 4,
            UcctClientId = 1,
            UcctFirstname = "Legacy",
            UcctSurname = "User",
            UcctEmail = "legacy@test.com",
            UserName = "legacy@test.com",
            Active = true,
            HasEmail = true,
            ValidatedEmail = true,
            StaffId = 12,
            Created = DateTime.Now,
            CreatedBy = "test",
            LastModified = DateTime.Now,
            LastModifiedBy = "test"
        };

        context.TucClientContacts.AddRange(activeContact, inactiveContact, staffContact, legacyContact);

        var activeCourier = new TucCourier
        {
            UccrId = 1,
            Code = "CR001",
            UccrName = "Test",
            UccrSurname = "Courier",
            UccrEmail = "courier@test.com",
            Active = true,
            Created = DateTime.Now,
            CreatedBy = "test",
            LastModified = DateTime.Now,
            LastModifiedBy = "test"
        };

        var inactiveCourier = new TucCourier
        {
            UccrId = 2,
            Code = "CR002",
            UccrName = "Inactive",
            UccrSurname = "Courier",
            UccrEmail = "inactive@test.com",
            Active = false,
            Created = DateTime.Now,
            CreatedBy = "test",
            LastModified = DateTime.Now,
            LastModifiedBy = "test"
        };

        context.TucCouriers.AddRange(activeCourier, inactiveCourier);

        context.TblSettings.Add(new TblSetting
        {
            SettingId = 1,
            AccountsMode = 2,
            SystemName = "Test",
            Version = "1.0",
            ApplicationName = "TestApp",
            AdminEmail = "admin@test.com",
            DefaultDateRange = "30",
            CommunicationFileDirectory = "/tmp",
            InternetRoot = "https://test.com",
            EnquiryEmail = "enquiry@test.com",
            ContactUsEmailSubject = "Contact Us",
            InternetJobEmailSubject = "Job",
            InternetJobPoaemail = "poa@test.com",
            InternetJobPoaemailSubject = "POA",
            InternetClientDetailsEmail = "client@test.com",
            InternetClientDetailsSubject = "Client Details",
            JoinOurTeamEmail = "join@test.com",
            JoinOurTeamSubject = "Join",
            JoinOurTeamReplyFromEmail = "reply@test.com",
            JoinOurTeamReplySubject = "Reply",
            JoinOurTeamReplyMessage = "Thanks",
            JobDetailsReplyFromEmail = "job@test.com",
            JobFeedbackSubject = "Feedback",
            TrackAndTrackReplyFromEmail = "track@test.com",
            ReportDomain = "test",
            ReportUserName = "user",
            ReportPassword = "pass",
            Smtpserver = "smtp.test.com",
            NewsImageDirectory = "/news",
            StaffImageDirectory = "/staff",
            ToolTipImageDirectory = "/tooltip",
            UncheckDirectEmailMessage = "msg",
            UncheckDirectEmailReply = "reply@test.com",
            UncheckDirectEmailSubject = "Subject",
            PpdDescription = "PPD",
            PpdAppliedDescription = "PPD Applied",
            CreatedBy = "test",
            LastModifiedBy = "test",
            FaxHeadLogo = [0x00],
            FaxHeadLogoSmall = [0x00],
            LetterHeadLogo = [0x00]
        });

        context.TblAfterhoursCouriers.Add(new TblAfterhoursCourier
        {
            Id = 1,
            CourierId = 1,
            WeekDay = 1,
            StartTime = DateTime.Today.AddHours(17),
            EndTime = DateTime.Today.AddHours(21)
        });

        context.SaveChanges();
    }
}
