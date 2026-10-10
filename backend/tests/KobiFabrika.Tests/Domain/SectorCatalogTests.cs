using KobiFabrika.Domain.Businesses;

namespace KobiFabrika.Tests.Domain;

public sealed class SectorCatalogTests
{
    [Fact]
    public void Every_sector_is_in_catalog_once()
    {
        SectorCatalog.All.Select(s => s.Sector).ShouldBe(Enum.GetValues<BusinessSector>(), ignoreOrder: true);
    }

    [Theory]
    [InlineData(BusinessSector.HairBarber, BusinessTemplate.Service)]
    [InlineData(BusinessSector.BeautyCare, BusinessTemplate.Service)]
    [InlineData(BusinessSector.ClinicHealth, BusinessTemplate.Service)]
    [InlineData(BusinessSector.SportsFitness, BusinessTemplate.Service)]
    [InlineData(BusinessSector.AutoService, BusinessTemplate.Service)]
    [InlineData(BusinessSector.RetailStore, BusinessTemplate.Retail)]
    [InlineData(BusinessSector.CafeRestaurant, BusinessTemplate.Retail)]
    [InlineData(BusinessSector.Other, BusinessTemplate.General)]
    public void Sector_maps_to_template(BusinessSector sector, BusinessTemplate template) =>
        SectorCatalog.TemplateFor(sector).ShouldBe(template);

    [Theory]
    [InlineData(BusinessTemplate.Service, BusinessModules.Appointments)]
    [InlineData(BusinessTemplate.Retail, BusinessModules.Stock)]
    [InlineData(BusinessTemplate.General, BusinessModules.All)]
    public void Template_sets_default_modules(BusinessTemplate template, BusinessModules modules) =>
        SectorCatalog.DefaultModulesFor(template).ShouldBe(modules);

    [Fact]
    public void Changing_sector_after_setup_keeps_modules()
    {
        var business = new Business { Name = "Test", Slug = "test-isletme" };
        business.SetSector(BusinessSector.HairBarber, resetModules: true);
        business.Modules = BusinessModules.All;

        business.SetSector(BusinessSector.RetailStore, resetModules: false);

        business.Template.ShouldBe(BusinessTemplate.Retail);
        business.Modules.ShouldBe(BusinessModules.All);
    }
}
