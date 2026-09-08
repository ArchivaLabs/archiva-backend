-- Read-only survey of the Archiva production database.
--
-- Run this BEFORE remove-dev-seed-data.sql. It answers the questions that
-- decide whether the cleanup is safe: how many organisations exist, which of
-- them carry the development seed user, and whether any real member or
-- uploaded document is sitting inside one of them.
--
-- Nothing here writes. Safe to run against production.

DECLARE @SeedUserId NVARCHAR(450) = '05ed33c5-59fb-4a79-9411-dbf5c701c2c2';

-- 1. Every organisation, with its member and meeting counts.
SELECT
    o.Id,
    o.Name,
    MemberCount  = (SELECT COUNT(*) FROM OrganizationUsers ou WHERE ou.OrganizationId = o.Id),
    MeetingCount = (SELECT COUNT(*) FROM Meetings m         WHERE m.OrganizationId  = o.Id),
    DocCount     = (SELECT COUNT(*) FROM Documents d        WHERE d.OrganizationId  = o.Id),
    HasSeedUser  = CASE WHEN EXISTS (
                       SELECT 1 FROM OrganizationUsers ou
                       WHERE ou.OrganizationId = o.Id AND ou.UserId = @SeedUserId)
                   THEN 1 ELSE 0 END
FROM Organizations o
ORDER BY o.Id;

-- 2. Every membership row, so you can see which UserIds are real Entra object
--    ids and which are the hardcoded seed literal.
SELECT ou.OrganizationId, o.Name AS OrganizationName, ou.UserId, ou.Email, ou.UserName, ou.Role
FROM OrganizationUsers ou
JOIN Organizations o ON o.Id = ou.OrganizationId
ORDER BY ou.OrganizationId, ou.Email;

-- 3. Any organisation holding the seed user that ALSO has another member.
--    The cleanup script refuses to run if this returns rows — deleting such an
--    organisation would destroy a real user's data.
SELECT DISTINCT ou.OrganizationId, o.Name
FROM OrganizationUsers ou
JOIN Organizations o ON o.Id = ou.OrganizationId
WHERE ou.OrganizationId IN (
        SELECT OrganizationId FROM OrganizationUsers WHERE UserId = @SeedUserId)
  AND ou.UserId <> @SeedUserId;

-- 4. Documents inside seed organisations. Their blobs live in Azure Storage and
--    are NOT removed by the cleanup script — delete these blob names by hand
--    afterwards, or you will leave orphaned blobs behind.
SELECT d.Id, d.OrganizationId, d.MeetingId, d.FileName, d.BlobName
FROM Documents d
WHERE d.OrganizationId IN (
        SELECT OrganizationId FROM OrganizationUsers WHERE UserId = @SeedUserId);
