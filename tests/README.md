# Backend test suite

Run `dotnet test Archiva.slnx` with Docker running. The functional and infrastructure projects start isolated SQL Server and Azurite containers through `TestAppHost`; they migrate and reset their own databases and never use the development database or live Azure services.

For a fast edit loop, run `dotnet test tests/Application.UnitTests`. Run `dotnet test tests/Application.FunctionalTests` and `dotnet test tests/Infrastructure.IntegrationTests` before merging. The functional suite uses a local test identity by default; its bearer validation tests use locally signed JWTs. The CI workflow runs all three projects, checks that each discovers tests, and uploads TRX and Cobertura coverage artifacts.
