using Hub.Models;

namespace Hub.Interfaces;

public interface IDespatchRepository
{
    Task<TucClientContact?> FetchUserByUsernameAsync(string email);
    Task<string> FetchSubAccountsAsync(int clientId);
    Task<List<RVW_stpValidateInternetPermissionsResult>> GetDespatchWebInternetPermissionsAsync(int contactId);
    Task InitiatePasswordResetAsync(int contactId, string recoveryEmail, string replyEmail, string link);
    Task UpdateUserAccessedAsync(int id, bool rememberMe, int tenantId);
    Task<int?> ValidateCourierByEmailAsync(string email);
    Task<int?> GetAccountsModeAsync();
    Task<bool> IsAfterHoursAuthorizedAsync(int courierId);
}
