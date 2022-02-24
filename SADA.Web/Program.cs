using Microsoft.EntityFrameworkCore;
using SADA.Core.Interfaces;
using SADA.DataAccess;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using SADA.Service;
using Stripe;
using SADA.Service.Settings;
using SADA.Service.Interfaces;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages().AddRazorRuntimeCompilation();

//setup program to use DbContext and another sections in appsettings.json
builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(
    builder.Configuration.GetConnectionString("DefaultConnection")
    ));
builder.Services.Configure<MailSetting>(builder.Configuration.GetSection("MailSettings"));
builder.Services.Configure<StripeSetting>(builder.Configuration.GetSection("StripeSettings"));
builder.Services.Configure<TwilioSetting>(builder.Configuration.GetSection("TwilioSettings"));

builder.Services.AddIdentity<IdentityUser,IdentityRole>().AddDefaultTokenProviders()
    .AddEntityFrameworkStores<ApplicationDbContext>();


//register unitOfWork for our program 
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddSingleton<IEmailSender, EmailSender>();
builder.Services.AddSingleton<ISmsSender, SmsSender>();
builder.Services.AddScoped<IDbInitializer, DbInitializer>();

builder.Services.ConfigureApplicationCookie( options =>
{ 
    options.LoginPath = $"/Identity/Account/Login";
    options.LogoutPath = $"/Identity/Account/Logout";
    options.AccessDeniedPath = $"/Identity/Account/AccessDenied";
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

SeedDatabase();
StripeConfiguration.ApiKey = builder.Configuration.GetSection("StripeSettings:SecretKey").Get<string>();

app.UseAuthentication(); //always Authentication come first
app.UseAuthorization();

app.MapRazorPages(); //added
app.MapControllerRoute(
    name    : "default",
    pattern : "{area=Client}/{controller=Home}/{action=Index}/{id?}");

app.Run();


void SeedDatabase()
{
    using (var scope = app.Services.CreateScope())
    {
        var dbInitializer = scope.ServiceProvider.GetRequiredService<IDbInitializer>();
        dbInitializer.Initialize();
    }
}
