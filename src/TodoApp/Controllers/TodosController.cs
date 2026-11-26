using DotNetCoreSqlDb.Data;
using DotNetCoreSqlDb.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace DotNetCoreSqlDb.Controllers
{
    [Authorize]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public class TodosController : Controller
    {
        private readonly ILogger<TodosController> _logger;
        private readonly MyDatabaseContext _context;

        public TodosController(MyDatabaseContext context, ILogger<TodosController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // GET: Todos
        public async Task<IActionResult> Index()
        {
            return View(await BuildIndexViewModel());
        }

        // POST: Todos/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            [Bind("Description", Prefix = "NewTodo")] Todo todo)
        {
            if (ModelState.IsValid)
            {
                todo.OwnerId = CurrentUserId;
                _context.Add(todo);
                await _context.SaveChangesAsync();

                return RedirectToAction(nameof(Index));
            }

            return View(nameof(Index), await BuildIndexViewModel(todo));
        }

        // POST: Todos/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var todo = await _context.Todo.SingleOrDefaultAsync(
                item => item.ID == id && item.OwnerId == CurrentUserId);
            if (todo == null)
            {
                return NotFound();
            }

            _context.Todo.Remove(todo);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        private async Task<TodosIndexViewModel> BuildIndexViewModel(Todo? newTodo = null)
        {
            _logger.LogInformation("Data from database.");
            return new TodosIndexViewModel
            {
                NewTodo = newTodo ?? new Todo(),
                Todos = await _context.Todo.AsNoTracking()
                    .Where(todo => todo.OwnerId == CurrentUserId)
                    .OrderByDescending(todo => todo.ID)
                    .ToListAsync()
            };
        }

        private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("The authenticated account has no user identifier.");
    }
}
