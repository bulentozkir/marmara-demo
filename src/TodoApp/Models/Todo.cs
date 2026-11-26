namespace DotNetCoreSqlDb.Models
{
    public class Todo
    {
        public int ID { get; set; }
        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Give your Todo a description.")]
        [System.ComponentModel.DataAnnotations.Display(Name = "What needs to be done?")]
        public string Description { get; set; } = default!;
        public string? OwnerId { get; set; }
    }
}
