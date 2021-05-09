using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using FanBento.Database;
using FanBento.Database.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace FanBento.Website.Controllers
{
    public class PostsController : Controller
    {
        private readonly FanBentoDatabase _context;
        private readonly IWebHostEnvironment _environment;

        public PostsController(FanBentoDatabase context, IWebHostEnvironment environment)
        {
            _context = context;
            _environment = environment;
        }

        // GET: Posts
        public async Task<IActionResult> Index()
        {
            if (_environment.IsDevelopment())
                return View(await _context.Post.OrderByDescending(t => t.UpdatedDatetime).ToListAsync());
            return NotFound();
        }

        // GET: Posts/Details/5
        public async Task<IActionResult> Details(string id)
        {
            if (id == null) return NotFound();

            var post = await _context.Post
                .Include(t => t.User)
                .Include(t => t.Body).ThenInclude(t => t.Files)
                .Include(t => t.Body).ThenInclude(t => t.Images)
                .Include(t => t.Body).ThenInclude(t => t.Blocks).ThenInclude(t => t.Styles)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (post == null) return NotFound();

            // Reorder unordered lists
            post.Body.Blocks?.ReOrder();
            post.Body.Images?.ReOrder();
            post.Body.Files?.ReOrder();

            // Parse texts to html
            if (!string.IsNullOrWhiteSpace(post.Body.Text))
            {
                post.Body.Text = HttpUtility.HtmlEncode(post.Body.Text);
                post.Body.Text = post.Body.Text.Replace("\n", "<br>");
            }

            return View(post);
        }
    }
}