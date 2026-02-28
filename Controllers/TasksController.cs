using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskManagerAPI.Data;
using TaskManagerAPI.DTOs;
using TaskManagerAPI.Models;

namespace TaskManagerAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class TasksController : ControllerBase
    {
        private readonly AppDbContext _db;

        public TasksController(AppDbContext db)
        {
            _db = db;
        }

        private int GetCurrentUserId() =>
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        private bool IsAdmin() =>
            User.IsInRole("Admin");

        // ── Map entity to response ──
        private static TaskResponseDto MapToDto(TaskItem t) => new()
        {
            Id = t.Id,
            Title = t.Title,
            Description = t.Description,
            Status = t.Status.ToString(),
            Priority = t.Priority.ToString(),
            DueDate = t.DueDate,
            CreatedAt = t.CreatedAt,
            AssignedTo = t.User?.FullName ?? ""
        };

        /// <summary>Get all tasks (Admin) or my tasks (User)</summary>
        [HttpGet]
        public async Task<IActionResult> GetTasks([FromQuery] string? status, [FromQuery] string? priority)
        {
            var query = _db.Tasks.Include(t => t.User).AsQueryable();

            if (!IsAdmin())
                query = query.Where(t => t.UserId == GetCurrentUserId());

            if (!string.IsNullOrEmpty(status) && Enum.TryParse<Models.TaskStatus>(status, true, out var s))
                query = query.Where(t => t.Status == s);

            if (!string.IsNullOrEmpty(priority) && Enum.TryParse<TaskPriority>(priority, true, out var p))
                query = query.Where(t => t.Priority == p);

            var tasks = await query.OrderByDescending(t => t.CreatedAt).ToListAsync();
            return Ok(tasks.Select(MapToDto));
        }

        /// <summary>Get task by ID</summary>
        [HttpGet("{id}")]
        public async Task<IActionResult> GetTask(int id)
        {
            var task = await _db.Tasks.Include(t => t.User).FirstOrDefaultAsync(t => t.Id == id);

            if (task == null) return NotFound(new { message = "Task not found." });
            if (!IsAdmin() && task.UserId != GetCurrentUserId())
                return Forbid();

            return Ok(MapToDto(task));
        }

        /// <summary>Create a new task</summary>
        [HttpPost]
        public async Task<IActionResult> CreateTask(CreateTaskDto dto)
        {
            var task = new TaskItem
            {
                Title = dto.Title,
                Description = dto.Description,
                Priority = (TaskPriority)dto.Priority,
                DueDate = dto.DueDate,
                UserId = GetCurrentUserId()
            };

            _db.Tasks.Add(task);
            await _db.SaveChangesAsync();
            await _db.Entry(task).Reference(t => t.User).LoadAsync();

            return CreatedAtAction(nameof(GetTask), new { id = task.Id }, MapToDto(task));
        }

        /// <summary>Update a task</summary>
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateTask(int id, UpdateTaskDto dto)
        {
            var task = await _db.Tasks.Include(t => t.User).FirstOrDefaultAsync(t => t.Id == id);

            if (task == null) return NotFound(new { message = "Task not found." });
            if (!IsAdmin() && task.UserId != GetCurrentUserId())
                return Forbid();

            if (dto.Title != null) task.Title = dto.Title;
            if (dto.Description != null) task.Description = dto.Description;
            if (dto.Status.HasValue) task.Status = (Models.TaskStatus)dto.Status.Value;
            if (dto.Priority.HasValue) task.Priority = (TaskPriority)dto.Priority.Value;
            if (dto.DueDate.HasValue) task.DueDate = dto.DueDate;

            task.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return Ok(MapToDto(task));
        }

        /// <summary>Delete a task</summary>
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteTask(int id)
        {
            var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == id);

            if (task == null) return NotFound(new { message = "Task not found." });
            if (!IsAdmin() && task.UserId != GetCurrentUserId())
                return Forbid();

            _db.Tasks.Remove(task);
            await _db.SaveChangesAsync();

            return Ok(new { message = "Task deleted successfully." });
        }

        /// <summary>Get tasks summary/stats</summary>
        [HttpGet("stats")]
        public async Task<IActionResult> GetStats()
        {
            var query = _db.Tasks.AsQueryable();
            if (!IsAdmin()) query = query.Where(t => t.UserId == GetCurrentUserId());

            return Ok(new
            {
                Total = await query.CountAsync(),
                Todo = await query.CountAsync(t => t.Status == Models.TaskStatus.Todo),
                InProgress = await query.CountAsync(t => t.Status == Models.TaskStatus.InProgress),
                Done = await query.CountAsync(t => t.Status == Models.TaskStatus.Done),
                HighPriority = await query.CountAsync(t => t.Priority == TaskPriority.High)
            });
        }
    }
}
