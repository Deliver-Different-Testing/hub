using Hub.Models;

namespace Hub.Interfaces;

public interface IDespatchRepository
{
    Task<TucClientContact?> FetchUserByUsername(string email);
    Task<string> FetchSubAccountsAsync(int clientId);
    Task<List<RVW_stpValidateInternetPermissionsResult>> GetDespatchWebInternetPermissions(int contactId);
    Task InitiatePasswordReset(int contactId, string recoveryEmail, string replyEmail, string link);
    Task UpdateUserAccessedAsync(int id, bool rememberMe);
    Task<int?> ValidateCourierByEmail(string email);
    Task<int?> GetAccountsModeAsync();
    Task<bool> IsAfterHoursAuthorized(int courierId);
}
