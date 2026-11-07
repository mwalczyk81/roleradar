using System.ComponentModel.DataAnnotations;
using Duende.IdentityServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RoleRadar.Identity.Data;
using RoleRadar.Identity.Services;

namespace RoleRadar.Identity.Pages.Account;

[AllowAnonymous]
public class LoginModel(
    SignInManager<IdentityUser<Guid>> signInManager,
    UserManager<IdentityUser<Guid>> userManager,
    IdentityAppDbContext dbContext,
    IIdentityServerInteractionService interactionService) : PageModel
{
    private readonly SignInManager<IdentityUser<Guid>> _signInManager = signInManager;
    private readonly UserManager<IdentityUser<Guid>> _userManager = userManager;
    private readonly IdentityAppDbContext _dbContext = dbContext;
    private readonly IIdentityServerInteractionService _interactionService = interactionService;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty]
    public TwoFactorModel TwoFactorInput { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [BindProperty]
    public bool RequiresTwoFactor { get; set; }

    public sealed class InputModel
    {
        [Required]
        public string Username { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;
    }

    public sealed class TwoFactorModel
    {
        [Required]
        [StringLength(7, MinimumLength = 6)]
        public string Code { get; set; } = string.Empty;
    }

    public void OnGet(string? returnUrl = null)
    {
        ReturnUrl = returnUrl ?? "/";
    }

    public async Task<IActionResult> OnPostAsync()
    {
        ReturnUrl ??= "/";

        if (!RequiresTwoFactor)
        {
            // Razor Pages binds and validates both input models on every post. The password step's form has no code
            // field, so binding has already recorded "The Code field is required" and the length message for it. With
            // no TwoFactorInput.* value posted, MVC binds that model with an empty prefix, so the key is plain "Code"
            // (not "TwoFactorInput.Code"); both forms are dropped. The page lists every ModelState error, so only this
            // step's own errors may show. The Input entries stay, and the code step (RequiresTwoFactor) keeps its
            // field messages.
            var codeKeys = ModelState.Keys
                .Where(k => k == nameof(TwoFactorModel.Code)
                    || k == nameof(TwoFactorInput)
                    || k.StartsWith(nameof(TwoFactorInput) + ".", StringComparison.Ordinal))
                .ToList();
            foreach (var key in codeKeys)
            {
                ModelState.Remove(key);
            }
        }

        if (RequiresTwoFactor)
        {
            if (string.IsNullOrWhiteSpace(TwoFactorInput.Code))
            {
                ModelState.AddModelError(string.Empty, "Please enter your 6-digit authenticator code.");
                return Page();
            }

            var cleanCode = TwoFactorInput.Code.Replace(" ", string.Empty).Replace("-", string.Empty);
            var twoFactorUser = await _signInManager.GetTwoFactorAuthenticationUserAsync();
            if (twoFactorUser is not null)
            {
                if (await _userManager.IsLockedOutAsync(twoFactorUser))
                {
                    ModelState.AddModelError(string.Empty, "Account is locked due to multiple failed login attempts. Please try again after 15 minutes.");
                    return Page();
                }

                var provisioningService = new OwnerProvisioningService(_dbContext);
                if (await provisioningService.VerifyTotpAndActivateAsync(twoFactorUser.Id, cleanCode))
                {
                    await _userManager.ResetAccessFailedCountAsync(twoFactorUser);
                    await _signInManager.SignInAsync(twoFactorUser, isPersistent: false);
                    return ValidateAndRedirect(ReturnUrl);
                }

                await _userManager.AccessFailedAsync(twoFactorUser);
                if (await _userManager.IsLockedOutAsync(twoFactorUser))
                {
                    ModelState.AddModelError(string.Empty, "Account is locked due to multiple failed login attempts. Please try again after 15 minutes.");
                    return Page();
                }

                ModelState.AddModelError(string.Empty, "Invalid verification code.");
                return Page();
            }

            var twoFactorResult = await _signInManager.TwoFactorAuthenticatorSignInAsync(
                cleanCode,
                isPersistent: false,
                rememberClient: false);

            if (twoFactorResult.Succeeded)
            {
                return ValidateAndRedirect(ReturnUrl);
            }

            if (twoFactorResult.IsLockedOut)
            {
                ModelState.AddModelError(string.Empty, "Account is locked due to multiple failed login attempts. Please try again after 15 minutes.");
                return Page();
            }

            ModelState.AddModelError(string.Empty, "Invalid verification code.");
            return Page();
        }

        if (string.IsNullOrWhiteSpace(Input.Username) || string.IsNullOrWhiteSpace(Input.Password))
        {
            ModelState.AddModelError(string.Empty, "Username and password are required.");
            return Page();
        }

        var result = await _signInManager.PasswordSignInAsync(
            Input.Username.Trim(),
            Input.Password,
            isPersistent: false,
            lockoutOnFailure: true);

        if (result.RequiresTwoFactor)
        {
            RequiresTwoFactor = true;
            ModelState.Clear();
            return Page();
        }

        if (result.Succeeded)
        {
            return ValidateAndRedirect(ReturnUrl);
        }

        if (result.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty, "Account is locked due to multiple failed login attempts. Please try again after 15 minutes.");
            return Page();
        }

        ModelState.AddModelError(string.Empty, "Invalid username or password.");
        return Page();
    }

    private RedirectResult ValidateAndRedirect(string returnUrl)
    {
        if (_interactionService.IsValidReturnUrl(returnUrl) || Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return Redirect("~/");
    }
}
