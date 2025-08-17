using HammerDrop_Auction_app.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace HammerDrop_Auction_app.Controllers
{
    [Authorize]
    public class AdsController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;

        public AdsController(AppDbContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        public async Task<IActionResult> Index()
        {
            var ads = _context.Ads
                .Include(a => a.Subcategory).ThenInclude(s => s.Category)
                .Include(a => a.Country)
                .Include(a => a.State)
                .Include(a => a.City)
                .Include(a => a.Images);

            return View(await ads.ToListAsync());
        }

        public IActionResult Create(int subcategoryId)
        {
            // Check if subcategory exists in database
            if (!_context.Subcategories.Any(s => s.Id == subcategoryId))
                return NotFound("Invalid subcategory");

            // Your helper method to load subcategory details into ViewBag
            LoadSubcategoryInfo(subcategoryId);

            // Set username in ViewBag
            ViewBag.UserName = User.Identity?.Name ?? "Anonymous";

            // Load location dropdowns
            LoadLocationDropdowns();

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Ad ad, List<IFormFile> uploadedImages, int AuctionDuration, string SaleType)
        {
            if (!_context.Subcategories.Any(s => s.Id == ad.SubcategoryId))
            {
                ModelState.AddModelError("SubcategoryId", "Invalid subcategory.");
            }

            // Map SaleType to IsAuction
            if (SaleType == "auction")
            {
                ad.IsAuction = true;
                ad.AuctionEndTime = DateTime.Now.AddDays(AuctionDuration);
            }
            else
            {
                ad.IsAuction = false;
                ad.Price = ad.Price; // optional, just to clarify fixed price usage
            }

            if (ModelState.IsValid)
            {
                // Set username
                ad.Name = User.Identity?.Name ?? "Anonymous";

                _context.Add(ad);
                await _context.SaveChangesAsync();

                // Save uploaded images
                await SaveUploadedImages(ad, uploadedImages);

                return RedirectToAction(nameof(Index));
            }

            LoadSubcategoryInfo(ad.SubcategoryId);
            ViewBag.UserName = User.Identity?.Name ?? "Anonymous";
            LoadLocationDropdowns(ad.CountryId, ad.StateId);

            return View(ad);
        }

        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var ad = await _context.Ads
                .Include(a => a.Images)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (ad == null) return NotFound();

            LoadSubcategoryInfo(ad.SubcategoryId);
            LoadLocationDropdowns(ad.CountryId, ad.StateId);
            return View(ad);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Ad ad, List<IFormFile> uploadedImages)
        {
            if (id != ad.Id) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(ad);
                    await _context.SaveChangesAsync();
                    await SaveUploadedImages(ad, uploadedImages);
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.Ads.Any(e => e.Id == ad.Id))
                        return NotFound();
                    else
                        throw;
                }
                return RedirectToAction(nameof(Index));
            }

            LoadSubcategoryInfo(ad.SubcategoryId);
            ViewBag.UserName = User.Identity?.Name ?? "Anonymous";
            LoadLocationDropdowns(ad.CountryId, ad.StateId);
            return View(ad);
        }

        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var ad = await _context.Ads
                .Include(a => a.Subcategory)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (ad == null) return NotFound();

            return View(ad);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var ad = await _context.Ads.Include(a => a.Images).FirstOrDefaultAsync(a => a.Id == id);

            if (ad != null)
            {
                foreach (var img in ad.Images)
                {
                    var path = Path.Combine(_env.WebRootPath, "images/ads", img.ImageName);
                    if (System.IO.File.Exists(path))
                        System.IO.File.Delete(path);

                    _context.AdImagess.Remove(img); // <-- Changed from ProductImages
                }

                _context.Ads.Remove(ad);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Index));
        }


        private void LoadLocationDropdowns(int? countryId = null, int? stateId = null)  // If a country is already selected (e.g., in edit), it will pre-select it.
        {
            ViewBag.CountryId = _context.Countries
                .OrderBy(c => c.name)
                .Select(c => new SelectListItem
                {
                    Value = c.id.ToString(),
                    Text = c.name,
                    Selected = (countryId != null && c.id == countryId)
                }).ToList();

            ViewBag.StateId = _context.States
                .Where(s => countryId == null || s.country_id == countryId)
                .OrderBy(s => s.name)
                .Select(s => new SelectListItem
                {
                    Value = s.id.ToString(),
                    Text = s.name,
                    Selected = (stateId != null && s.id == stateId)
                }).ToList();

            ViewBag.CityId = _context.Cities
                .Where(c => stateId == null || c.state_id == stateId)
                .OrderBy(c => c.name)
                .Select(c => new SelectListItem
                {
                    Value = c.id.ToString(),
                    Text = c.name
                }).ToList();
        }

        private void LoadSubcategoryInfo(int subcategoryId)
        {
            var subcategory = _context.Subcategories.FirstOrDefault(s => s.Id == subcategoryId);
            ViewBag.SubcategoryTitle = subcategory?.Title ?? "";
            ViewBag.SubcategoryId = subcategoryId;
        }
        private async Task SaveUploadedImages(Ad ad, List<IFormFile> uploadedImages)
        {
            if (uploadedImages != null && uploadedImages.Count > 0)
            {
                foreach (var file in uploadedImages)
                {
                    if (file.Length > 0)
                    {
                        var fileName = Guid.NewGuid().ToString() + Path.GetExtension(file.FileName);
                        var path = Path.Combine(_env.WebRootPath, "images/ads", fileName);

                        using (var stream = new FileStream(path, FileMode.Create))
                        {
                            await file.CopyToAsync(stream);
                        }

                        _context.AdImagess.Add(new AdImages
                        {
                            AdId = ad.Id, // <-- Changed from ProductId
                            ImageName = fileName
                        });
                    }
                }
                await _context.SaveChangesAsync();
            }
        }

        [HttpGet]
        public JsonResult GetStatesByCountry(int countryId)
        {
            var states = _context.States
                .Where(s => s.country_id == countryId)
                .OrderBy(s => s.name)
                .Select(s => new { s.id, s.name })
                .ToList();
            return Json(states);
        }

        [HttpGet]
        public JsonResult GetCitiesByState(int stateId)
        {
            var cities = _context.Cities
                .Where(c => c.state_id == stateId)
                .OrderBy(c => c.name)
                .Select(c => new { c.id, c.name })
                .ToList();
            return Json(cities);
        }
    }
}
