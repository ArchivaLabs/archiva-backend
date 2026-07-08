using Archiva.Domain.Entities;
using Archiva.Domain.Enums;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Archiva.Infrastructure.Data;

public static class InitialiserExtensions
{
    public static async Task InitialiseDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();

        var initialiser =
            scope.ServiceProvider.GetRequiredService<ApplicationDbContextInitialiser>();

        await initialiser.InitialiseAsync();
        await initialiser.SeedAsync();
    }
}

public class ApplicationDbContextInitialiser
{
    private readonly ILogger<ApplicationDbContextInitialiser> _logger;
    private readonly ApplicationDbContext _context;

    public ApplicationDbContextInitialiser(
        ILogger<ApplicationDbContextInitialiser> logger,
        ApplicationDbContext context
    )
    {
        _logger = logger;
        _context = context;
    }

    public async Task InitialiseAsync()
    {
        try
        {
            await _context.Database.EnsureDeletedAsync();
            await _context.Database.EnsureCreatedAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while initialising the database.");
            throw;
        }
    }

    public async Task SeedAsync()
    {
        try
        {
            await TrySeedAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while seeding the database.");
            throw;
        }
    }

    public async Task TrySeedAsync()
    {
        // ── 1. Organisation ───────────────────────────────────────────────
        var org = new Organization { Name = "University of Abuja", LogoUrl = null };
        _context.Organizations.Add(org);
        await _context.SaveChangesAsync();

        // ── 2. Seed user (matches your real Microsoft account) ────────────
        var seedUserId = "05ed33c5-59fb-4a79-9411-dbf5c701c2c2";
        var seedUserName = "Lansa ®";
        var seedEmail = "olamideiyanda18@gmail.com";

        var member = new OrganizationUser
        {
            OrganizationId = org.Id,
            UserId = seedUserId,
            UserName = seedUserName,
            Email = seedEmail,
            Role = UserRole.Admin,
            JoinedAt = DateTime.UtcNow,
        };
        _context.OrganizationUsers.Add(member);
        await _context.SaveChangesAsync();

        // ── 3. Default tags ───────────────────────────────────────────────
        var tagNames = new[]
        {
            // Business
            "Finance",
            "Legal",
            "HR",
            "Strategy",
            "Compliance",
            "Operations",
            "IT",
            "Procurement",
            "Marketing",
            "Executive",
            // University
            "Senate",
            "Faculty",
            "Research",
            "Audit",
            "Academic",
            "Governance",
            "Admissions",
            "Registry",
            "Examinations",
            "Student Affairs",
            "Infrastructure",
            "Council",
        };

        var tags = tagNames
            .Select(name => new Tag { Name = name, OrganizationId = org.Id })
            .ToList();

        _context.Tags.AddRange(tags);
        await _context.SaveChangesAsync();

        // ── 4. 30 seed meetings ───────────────────────────────────────────
        var tagLookup = tags.ToDictionary(t => t.Name);

        var seedMeetings = new List<(
            string Title,
            string? Description,
            DateTime Date,
            TimeSpan Time,
            string? Location,
            string[] Tags
        )>
        {
            (
                "Senate General Assembly",
                "Full senate session for Q3 review.",
                DateTime.UtcNow.AddDays(-60),
                new TimeSpan(10, 0, 0),
                "Senate Chamber, Block A",
                ["Senate", "Governance"]
            ),
            (
                "Budget Planning Committee",
                "Annual budget allocation discussion.",
                DateTime.UtcNow.AddDays(-57),
                new TimeSpan(9, 0, 0),
                "Finance Office, Room 201",
                ["Finance", "Executive"]
            ),
            (
                "Faculty Board Meeting",
                "Review of academic programmes.",
                DateTime.UtcNow.AddDays(-54),
                new TimeSpan(11, 0, 0),
                "Faculty Hall, Room 105",
                ["Faculty", "Academic"]
            ),
            (
                "IT Infrastructure Review",
                "Assessment of campus network upgrade.",
                DateTime.UtcNow.AddDays(-51),
                new TimeSpan(14, 0, 0),
                "ICT Centre, Server Room B",
                ["IT", "Infrastructure"]
            ),
            (
                "Research Grant Proposals",
                "Evaluation of submitted grant applications.",
                DateTime.UtcNow.AddDays(-48),
                new TimeSpan(10, 30, 0),
                "Research Block, Conference Room 3",
                ["Research", "Finance"]
            ),
            (
                "Student Affairs Committee",
                "Student welfare and support services review.",
                DateTime.UtcNow.AddDays(-45),
                new TimeSpan(13, 0, 0),
                "Student Union Building, Room 12",
                ["Student Affairs", "Governance"]
            ),
            (
                "Admissions Policy Review",
                "Update admission criteria for 2027 intake.",
                DateTime.UtcNow.AddDays(-42),
                new TimeSpan(9, 30, 0),
                "Admissions Office, Room 4",
                ["Admissions", "Strategy"]
            ),
            (
                "Procurement Audit",
                "Internal audit of procurement processes.",
                DateTime.UtcNow.AddDays(-39),
                new TimeSpan(10, 0, 0),
                "Audit Suite, Administration Block",
                ["Procurement", "Audit"]
            ),
            (
                "Legal Compliance Workshop",
                "Annual compliance training for staff.",
                DateTime.UtcNow.AddDays(-36),
                new TimeSpan(15, 0, 0),
                "Law Faculty Seminar Room 2",
                ["Legal", "Compliance"]
            ),
            (
                "Council Extraordinary Session",
                "Emergency council session on facilities.",
                DateTime.UtcNow.AddDays(-33),
                new TimeSpan(8, 0, 0),
                "Council Chambers, Main Block",
                ["Council", "Executive"]
            ),
            (
                "HR Policy Update",
                "Review of updated HR policies and procedures.",
                DateTime.UtcNow.AddDays(-30),
                new TimeSpan(11, 0, 0),
                "HR Department, Room 301",
                ["HR", "Compliance"]
            ),
            (
                "Examinations Board Meeting",
                "Setting examination timetable and policies.",
                DateTime.UtcNow.AddDays(-28),
                new TimeSpan(10, 0, 0),
                "Examinations Office, Block C",
                ["Examinations", "Academic"]
            ),
            (
                "Registry Digitisation Project",
                "Progress update on records digitisation.",
                DateTime.UtcNow.AddDays(-26),
                new TimeSpan(14, 0, 0),
                "Registry Department, Room 102",
                ["Registry", "IT"]
            ),
            (
                "Marketing Strategy Session",
                "Planning university outreach campaigns.",
                DateTime.UtcNow.AddDays(-24),
                new TimeSpan(13, 30, 0),
                "Communications Office, Room 205",
                ["Marketing", "Strategy"]
            ),
            (
                "Operations Review Q2",
                "Quarterly operations performance review.",
                DateTime.UtcNow.AddDays(-22),
                new TimeSpan(9, 0, 0),
                "Operations Centre, Block D",
                ["Operations", "Executive"]
            ),
            (
                "Research Ethics Committee",
                "Review of research ethics submissions.",
                DateTime.UtcNow.AddDays(-20),
                new TimeSpan(10, 0, 0),
                "Research Block, Ethics Suite",
                ["Research", "Governance"]
            ),
            (
                "Finance Audit Meeting",
                "External auditors presentation of findings.",
                DateTime.UtcNow.AddDays(-18),
                new TimeSpan(11, 0, 0),
                "Finance Office, Boardroom",
                ["Finance", "Audit"]
            ),
            (
                "Senate Committee on Curriculum",
                "Curriculum reform proposals review.",
                DateTime.UtcNow.AddDays(-16),
                new TimeSpan(10, 0, 0),
                "Senate Chamber, Block A",
                ["Senate", "Faculty", "Academic"]
            ),
            (
                "IT Security Briefing",
                "Cybersecurity threat landscape overview.",
                DateTime.UtcNow.AddDays(-14),
                new TimeSpan(14, 0, 0),
                "ICT Centre, Briefing Room",
                ["IT", "Compliance"]
            ),
            (
                "Student Union Liaison",
                "Meeting with student union representatives.",
                DateTime.UtcNow.AddDays(-12),
                new TimeSpan(15, 0, 0),
                "Student Union Building, Main Hall",
                ["Student Affairs", "Governance"]
            ),
            (
                "Infrastructure Development Plan",
                "Five-year campus development strategy.",
                DateTime.UtcNow.AddDays(-10),
                new TimeSpan(9, 0, 0),
                "Works Department, Planning Room",
                ["Infrastructure", "Strategy"]
            ),
            (
                "HR Recruitment Drive",
                "Planning for new academic year hiring.",
                DateTime.UtcNow.AddDays(-9),
                new TimeSpan(10, 0, 0),
                "HR Department, Room 302",
                ["HR", "Faculty"]
            ),
            (
                "Legal Affairs Update",
                "Review of pending legal matters.",
                DateTime.UtcNow.AddDays(-8),
                new TimeSpan(11, 0, 0),
                "Legal Unit, Administration Block",
                ["Legal", "Executive"]
            ),
            (
                "Procurement Committee",
                "Vendor selection for lab equipment.",
                DateTime.UtcNow.AddDays(-7),
                new TimeSpan(13, 0, 0),
                "Procurement Office, Room 108",
                ["Procurement", "Finance"]
            ),
            (
                "Admissions Open Day Planning",
                "Logistics for upcoming open day event.",
                DateTime.UtcNow.AddDays(-6),
                new TimeSpan(10, 0, 0),
                "Admissions Office, Conference Room",
                ["Admissions", "Marketing"]
            ),
            (
                "Research Symposium Planning",
                "Annual research symposium preparations.",
                DateTime.UtcNow.AddDays(-5),
                new TimeSpan(14, 0, 0),
                "Research Block, Symposium Hall",
                ["Research", "Academic"]
            ),
            (
                "Council Budget Approval",
                "Final approval of annual university budget.",
                DateTime.UtcNow.AddDays(-4),
                new TimeSpan(9, 0, 0),
                "Council Chambers, Main Block",
                ["Council", "Finance", "Executive"]
            ),
            (
                "Registry Compliance Review",
                "Ensuring student records compliance.",
                DateTime.UtcNow.AddDays(-3),
                new TimeSpan(11, 0, 0),
                "Registry Department, Room 103",
                ["Registry", "Compliance"]
            ),
            (
                "Senate Closing Session",
                "End of semester senate closing session.",
                DateTime.UtcNow.AddDays(-2),
                new TimeSpan(10, 0, 0),
                "Senate Chamber, Block A",
                ["Senate", "Governance"]
            ),
            (
                "Executive Strategy Retreat",
                "Annual executive team strategy planning.",
                DateTime.UtcNow.AddDays(-1),
                new TimeSpan(9, 0, 0),
                "Vice Chancellor's Conference Room",
                ["Executive", "Strategy"]
            ),
        };

        foreach (var (title, description, date, time, location, meetingTags) in seedMeetings)
        {
            var meeting = new Meeting
            {
                Title = title,
                Description = description,
                MeetingDate = date,
                MeetingTime = time,
                Location = location,
                OrganizationId = org.Id,
                CreatedBy = seedUserName,
                CreatedByAvatar = null,
            };
            _context.Meetings.Add(meeting);
            await _context.SaveChangesAsync();

            var meetingTagEntities = meetingTags
                .Where(tagLookup.ContainsKey)
                .Select(t => new MeetingTag { MeetingId = meeting.Id, TagId = tagLookup[t].Id })
                .ToList();

            _context.MeetingTags.AddRange(meetingTagEntities);
            await _context.SaveChangesAsync();
        }
    }
}
