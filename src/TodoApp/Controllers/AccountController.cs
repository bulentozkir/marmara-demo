using DotNetCoreSqlDb.Data;
using DotNetCoreSqlDb.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DotNetCoreSqlDb.Controllers
{
    [Authorize]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public class AccountController : Controller
    {
        private readonly UserManager<IdentityUser> _users;
        private readonly SignInManager<IdentityUser> _signIn;
        private readonly MyDatabaseContext _context;

        public AccountController(UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn,
            MyDatabaseContext context)
        {
            _users = users;
            _signIn = signIn;
            _context = context;
        }

        [AllowAnonymous, HttpGet]
        public IActionResult Login(string? returnUrl = null) =>
            User.Identity?.IsAuthenticated == true
                ? RedirectToAction("Index", "Todos")
                : View(new LoginViewModel { ReturnUrl = returnUrl });

        [AllowAnonymous, HttpPost]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (User.Identity?.IsAuthenticated == true)
                return RedirectToAction("Index", "Todos");
            if (!ModelState.IsValid)
                return View(model);

            var result = await _signIn.PasswordSignInAsync(
                model.Username, model.Password, isPersistent: false, lockoutOnFailure: true);
            if (result.Succeeded)
                return AfterSignIn(model.ReturnUrl);

            ModelState.AddModelError("", "The username or password is incorrect. Please try again.");
            return View(model);
        }

        [AllowAnonymous, HttpGet]
        public IActionResult Register(string? returnUrl = null) =>
            User.Identity?.IsAuthenticated == true
                ? RedirectToAction("Index", "Todos")
                : View(new RegisterViewModel { ReturnUrl = returnUrl });

        [AllowAnonymous, HttpPost]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (User.Identity?.IsAuthenticated == true)
                return RedirectToAction("Index", "Todos");
            if (!ModelState.IsValid)
                return View(model);

            var user = new IdentityUser { UserName = model.Username };
            var result = await _users.CreateAsync(user, model.Password);
            if (!result.Succeeded)
            {
                AddErrors(result);
                return View(model);
            }

            await _signIn.SignInAsync(user, isPersistent: false);
            TempData["StatusMessage"] = "Your account is ready. Make a little space for your next big idea.";
            return AfterSignIn(model.ReturnUrl);
        }

        [HttpPost]
        public async Task<IActionResult> Logout()
        {
            await _signIn.SignOutAsync();
            return RedirectToAction(nameof(Login));
        }

        [HttpGet]
        public async Task<IActionResult> Profile()
        {
            var user = await _users.GetUserAsync(User);
            return user is null ? Challenge() : View(ProfileFor(user));
        }

        [HttpPost]
        public async Task<IActionResult> UpdateProfile([Bind(Prefix = "Account")] UpdateAccountViewModel model)
        {
            var user = await _users.GetUserAsync(User);
            if (user is null)
                return Challenge();
            if (!ModelState.IsValid)
                return View("Profile", ProfileFor(user));
            if (!await _users.CheckPasswordAsync(user, model.CurrentPassword))
            {
                ModelState.AddModelError("Account.CurrentPassword", "Your current password is incorrect.");
                return View("Profile", ProfileFor(user));
            }

            var result = await _users.SetUserNameAsync(user, model.Username);
            if (!result.Succeeded)
            {
                AddErrors(result);
                return View("Profile", ProfileFor(user));
            }

            await _signIn.RefreshSignInAsync(user);
            TempData["StatusMessage"] = "Your username has been updated.";
            return RedirectToAction(nameof(Profile));
        }

        [HttpPost]
        public async Task<IActionResult> ChangePassword([Bind(Prefix = "Password")] ChangePasswordViewModel model)
        {
            var user = await _users.GetUserAsync(User);
            if (user is null)
                return Challenge();
            if (!ModelState.IsValid)
                return View("Profile", ProfileFor(user));

            var result = await _users.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
            if (!result.Succeeded)
            {
                AddErrors(result);
                return View("Profile", ProfileFor(user));
            }

            await _signIn.RefreshSignInAsync(user);
            TempData["StatusMessage"] = "Your password has been changed. Other sessions are signed out.";
            return RedirectToAction(nameof(Profile));
        }

        [HttpPost]
        public async Task<IActionResult> DeleteAccount([Bind(Prefix = "Deletion")] DeleteAccountViewModel model)
        {
            var user = await _users.GetUserAsync(User);
            if (user is null)
                return Challenge();
            if (!model.ConfirmDeletion)
                ModelState.AddModelError("Deletion.ConfirmDeletion", "Confirm account deletion to continue.");
            if (!ModelState.IsValid)
                return View("Profile", ProfileFor(user));
            if (!await _users.CheckPasswordAsync(user, model.CurrentPassword))
            {
                ModelState.AddModelError("Deletion.CurrentPassword", "Your current password is incorrect.");
                return View("Profile", ProfileFor(user));
            }

            _context.Todo.RemoveRange(await _context.Todo.Where(todo => todo.OwnerId == user.Id).ToListAsync());
            _context.Users.Remove(user);
            await _context.SaveChangesAsync();
            await _signIn.SignOutAsync();
            TempData["StatusMessage"] = "Your account and Todos have been deleted.";
            return RedirectToAction(nameof(Register));
        }

        private IActionResult AfterSignIn(string? returnUrl) =>
            Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction("Index", "Todos");

        private static ProfileViewModel ProfileFor(IdentityUser user) => new()
        {
            Account = new UpdateAccountViewModel
            {
                Username = user.UserName ?? throw new InvalidOperationException("The account has no username.")
            }
        };

        private void AddErrors(IdentityResult result)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError("", error.Description);
        }
    }
}
