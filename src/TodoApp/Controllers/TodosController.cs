using DotNetCoreSqlDb.Data;
using DotNetCoreSqlDb.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Azure;
using Azure.Identity;
using DotNetCoreSqlDb.Services;

namespace DotNetCoreSqlDb.Controllers
{
    [Authorize]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public class TodosController : Controller
    {
        private readonly ILogger<TodosController> _logger;
        private readonly MyDatabaseContext _context;
        private readonly ITodoSearch _search;

        public TodosController(MyDatabaseContext context, ILogger<TodosController> logger, ITodoSearch search)
        {
            _context = context;
            _logger = logger;
            _search = search;
        }

        // GET: Todos
        public async Task<IActionResult> Index(string? q = null, int page = 1)
        {
            return View(await BuildIndexViewModel(query: q, page: page));
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
                _context.QueueSearchUpdate(todo);
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
            _context.QueueSearchUpdate(todo);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        private async Task<TodosIndexViewModel> BuildIndexViewModel(
            Todo? newTodo = null, string? query = null, int page = 1)
        {
            _logger.LogInformation("Data from database.");
            var ownerId = CurrentUserId;
            var owned = _context.Todo.AsNoTracking().Where(todo => todo.OwnerId == ownerId);
            var model = new TodosIndexViewModel
            {
                NewTodo = newTodo ?? new Todo(),
                SearchText = query?.Trim() ?? "",
                SearchPage = Math.Clamp(page, 1, 400),
                TotalTodoCount = await owned.CountAsync()
            };
            if (model.SearchText.Length == 0)
            {
                model.Todos = await owned.OrderByDescending(todo => todo.ID).ToListAsync();
                model.SearchMatchCount = model.TotalTodoCount;
                return model;
            }
            if (model.SearchText.Length > 200)
            {
                Response.StatusCode = StatusCodes.Status400BadRequest;
                model.SearchError = "Use 200 characters or fewer in your search.";
                return model;
            }

            try
            {
                var hits = await _search.SearchAsync(
                    model.SearchText, ownerId, model.SearchPage, HttpContext.RequestAborted);
                var ids = hits.DocumentIds.ToArray();
                var current = await owned.Where(todo => ids.Contains(todo.SearchDocumentId))
                    .ToDictionaryAsync(todo => todo.SearchDocumentId);
                model.Todos = hits.DocumentIds.Where(current.ContainsKey).Select(id => current[id]).ToList();
                model.SearchMatchCount = hits.TotalMatches;
            }
            catch (RequestFailedException exception)
            {
                _logger.LogError("Azure AI Search failed with HTTP {Status}.", exception.Status);
                Response.StatusCode = exception.Status == 400
                    ? StatusCodes.Status400BadRequest : StatusCodes.Status503ServiceUnavailable;
                model.SearchError = exception.Status == 400
                    ? "That query could not be understood. Try a few simple words."
                    : "Search is temporarily unavailable. Your Todos are safe; clear the search to view your list.";
            }
            catch (AuthenticationFailedException exception)
            {
                _logger.LogError(exception, "Azure AI Search identity authentication failed.");
                Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                model.SearchError = "Search is temporarily unavailable. Your Todos are safe; clear the search to view your list.";
            }
            return model;
        }

        private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("The authenticated account has no user identifier.");
    }
}
