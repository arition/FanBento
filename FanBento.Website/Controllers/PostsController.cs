using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using System.Web;
using FanBento.Database;
using FanBento.Database.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace FanBento.Website.Controllers
{
    public class PostsController : Controller
    {
        private readonly FanBentoDatabase _context;
        private readonly IWebHostEnvironment _environment;
        private IConfiguration _configuration;

        public PostsController(FanBentoDatabase context, IWebHostEnvironment environment, IConfiguration configuration)
        {
            _configuration = configuration;
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

        /// <summary>Serves the original post at its existing URL.</summary>
        public Task<IActionResult> Details(string id) => RenderDetails(id);

        /// <summary>Serves saved Simplified Chinese text and returns 404 until a translation exists.</summary>
        [HttpGet("/zh-cn/posts/details/{id}")]
        public Task<IActionResult> ChineseDetails(string id) => RenderDetails(id, "zh-cn");

        /// <summary>Loads a detached post so rendering translated text cannot overwrite the original.</summary>
        private async Task<IActionResult> RenderDetails(string id, string language="default")
        {
            if (id == null) return NotFound();

            var post = await _context.Post.AsNoTracking()
                .Include(t => t.User)
                .Include(t => t.Body).ThenInclude(t => t.Files)
                .Include(t => t.Body).ThenInclude(t => t.Images)
                .Include(t => t.Body).ThenInclude(t => t.Blocks).ThenInclude(t => t.Styles)
                .Include(t => t.Body).ThenInclude(t => t.Blocks).ThenInclude(t => t.Links)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (post == null) return NotFound();

            if (language == "zh-cn")
            {
                // A failed refresh may still have valid content from an earlier successful attempt.
                var translation = await _context.PostTranslation.AsNoTracking()
                    .SingleOrDefaultAsync(t => t.PostId == id && t.Language == "zh-cn");
                if (translation?.ContentJson == null) return NotFound();
                TranslatedPostContent.Apply(post,
                    JsonSerializer.Deserialize<Dictionary<string, string>>(translation.ContentJson));
            }
            ViewBag.Language = language == "zh-cn" ? "zh-CN" : null;

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

            ViewBag.UrlPrefix = _configuration["Assets:UrlPrefix"];

            return View("Details", post);
        }
    }
}