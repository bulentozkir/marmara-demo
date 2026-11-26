namespace DotNetCoreSqlDb.Models
{
    public class TodosIndexViewModel
    {
        public Todo NewTodo { get; set; } = new();
        public IReadOnlyList<Todo> Todos { get; set; } = Array.Empty<Todo>();
        public int TotalTodoCount { get; set; }
        public string SearchText { get; set; } = "";
        public string? SearchError { get; set; }
        public long SearchMatchCount { get; set; }
        public int SearchPage { get; set; } = 1;
    }
}
