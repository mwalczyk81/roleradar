using Duende.IdentityServer.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace RoleRadar.Identity.Pages.Account;

[AllowAnonymous]
[IgnoreAntiforgeryToken]
public class LogoutModel(
    SignInManager<IdentityUser<Guid>> signInManager,
    IIdentityServerInteractionService interactionService) : PageModel
{
    private readonly SignInManager<IdentityUser<Guid>> _signInManager = signInManager;
    private readonly IIdentityServerInteractionService _interactionService = interactionService;

    [BindProperty(SupportsGet = true)]
    public string? LogoutId { get; set; }

    public async Task<IActionResult> OnGetAsync(string? logoutId = null)
    {
        return await ProcessLogoutAsync(logoutId ?? LogoutId);
    }

    public async Task<IActionResult> OnPostAsync(string? logoutId = null)
    {
        return await ProcessLogoutAsync(logoutId ?? LogoutId);
    }

    private async Task<IActionResult> ProcessLogoutAsync(string? logoutId)
    {
        var logoutContext = !string.IsNullOrWhiteSpace(logoutId)
            ? await _interactionService.GetLogoutContextAsync(logoutId, HttpContext.RequestAborted)
            : null;

        await _signInManager.SignOutAsync();
        await HttpContext.SignOutAsync(IdentityConstants.TwoFactorRememberMeScheme);
        await HttpContext.SignOutAsync(IdentityConstants.TwoFactorUserIdScheme);

        if (!string.IsNullOrWhiteSpace(logoutContext?.PostLogoutRedirectUri))
        {
            // Only redirect to the URI Duende has already validated against registered PostLogoutRedirectUris.
            return Redirect(logoutContext.PostLogoutRedirectUri);
        }

        // If there is no valid logout context or no post-logout URI, show a simple "You are signed out" page.
        return Page();
    }
}
