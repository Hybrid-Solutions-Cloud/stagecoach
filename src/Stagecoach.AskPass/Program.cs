using Stagecoach.Infrastructure.Security;

var value = Environment.GetEnvironmentVariable("STAGECOACH_ASKPASS_PROFILE");
if (!Guid.TryParse(value, out var profileId)) return 2;

var credential = await new WindowsCredentialManager().ReadAsync(profileId);
if (credential is null) return 3;

Console.Out.Write(credential.Value.Password);
return 0;
