-- Remove development seed data from the Archiva production database.
--
-- Why this exists
-- ---------------
-- ApplicationDbContextInitialiser seeds an organisation ("University of Abuja")
-- whose OrganizationUser row carries a HARDCODED UserId literal rather than a
-- real Entra object id. That data reached production. Because SyncUser used to
-- match membership on Email, a real Microsoft user was matched to that fake row,
-- synced as "existing", and was then rejected with 401 by every other endpoint
-- (all of which resolve membership by UserId). SyncUser is fixed; this removes
-- the orphaned data so affected users onboard cleanly as new.
--
-- Safety properties
-- -----------------
--   * Scoped to organisations that contain the seed UserId literal. Nothing else
--     is touched.
--   * ABORTS if any such organisation has a second, real member — that would be
--     a live organisation, not seed data.
--   * Runs in a single transaction: it commits entirely or not at all.
--   * Idempotent: running it again after a successful run deletes nothing.
--
-- Before running
-- --------------
--   1. Run scripts/inspect-seed-data.sql and read the output.
--   2. Take a database backup / restore point.
--   3. Note any rows from inspect step 4 — those blobs must be deleted from
--      Azure Storage separately. This script only removes database rows.

SET XACT_ABORT ON;
SET NOCOUNT ON;

DECLARE @SeedUserId NVARCHAR(450) = '05ed33c5-59fb-4a79-9411-dbf5c701c2c2';

BEGIN TRANSACTION;

-- Organisations that carry the seed user.
DECLARE @SeedOrgs TABLE (OrganizationId INT PRIMARY KEY);

INSERT INTO @SeedOrgs (OrganizationId)
SELECT DISTINCT OrganizationId
FROM OrganizationUsers
WHERE UserId = @SeedUserId;

IF NOT EXISTS (SELECT 1 FROM @SeedOrgs)
BEGIN
    PRINT 'No seed organisations found. Nothing to do.';
    COMMIT TRANSACTION;
    RETURN;
END

-- Guard: refuse to delete an organisation that has a real member in it.
IF EXISTS (
    SELECT 1
    FROM OrganizationUsers ou
    JOIN @SeedOrgs s ON s.OrganizationId = ou.OrganizationId
    WHERE ou.UserId <> @SeedUserId)
BEGIN
    ROLLBACK TRANSACTION;
    THROW 50001, 'Aborted: a seed organisation also contains a real member. Inspect manually before deleting.', 1;
END

-- Meetings belonging to those organisations.
DECLARE @SeedMeetings TABLE (MeetingId INT PRIMARY KEY);

INSERT INTO @SeedMeetings (MeetingId)
SELECT m.Id
FROM Meetings m
JOIN @SeedOrgs s ON s.OrganizationId = m.OrganizationId;

-- Delete children before parents.
DELETE mt
FROM MeetingTags mt
JOIN @SeedMeetings sm ON sm.MeetingId = mt.MeetingId;
PRINT CONCAT('MeetingTags deleted: ', @@ROWCOUNT);

DELETE d
FROM Documents d
JOIN @SeedOrgs s ON s.OrganizationId = d.OrganizationId;
PRINT CONCAT('Documents deleted (blobs NOT removed): ', @@ROWCOUNT);

DELETE m
FROM Meetings m
JOIN @SeedOrgs s ON s.OrganizationId = m.OrganizationId;
PRINT CONCAT('Meetings deleted: ', @@ROWCOUNT);

DELETE t
FROM Tags t
JOIN @SeedOrgs s ON s.OrganizationId = t.OrganizationId;
PRINT CONCAT('Tags deleted: ', @@ROWCOUNT);

DELETE ui
FROM UserInvitations ui
JOIN @SeedOrgs s ON s.OrganizationId = ui.OrganizationId;
PRINT CONCAT('UserInvitations deleted: ', @@ROWCOUNT);

DELETE ou
FROM OrganizationUsers ou
JOIN @SeedOrgs s ON s.OrganizationId = ou.OrganizationId;
PRINT CONCAT('OrganizationUsers deleted: ', @@ROWCOUNT);

DELETE o
FROM Organizations o
JOIN @SeedOrgs s ON s.OrganizationId = o.Id;
PRINT CONCAT('Organizations deleted: ', @@ROWCOUNT);

COMMIT TRANSACTION;
PRINT 'Seed data removed.';
