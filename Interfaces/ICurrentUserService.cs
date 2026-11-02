using System.Security.Claims;
using Hub.ViewModels;

namespace Hub.Interfaces;

public interface ICurrentUserService
{
    Task<MeResponse> GetMeAsync(ClaimsPrincipal user);
}
