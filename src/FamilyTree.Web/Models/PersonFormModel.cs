using System.ComponentModel.DataAnnotations;

namespace FamilyTree.Web.Models;

public sealed class PersonFormModel
{
    [Required, Display(Name = "First name")]
    public string FirstName { get; set; } = "";

    [Display(Name = "Middle name(s)")]
    public string? MiddleName { get; set; }

    [Required, Display(Name = "Last name")]
    public string LastName { get; set; } = "";

    [Display(Name = "Maiden name")]
    public string? MaidenName { get; set; }

    public string Sex { get; set; } = "unknown";

    [Display(Name = "Born on")]
    public string? BornOn { get; set; }

    [Display(Name = "Born in")]
    public string? BornPlace { get; set; }

    [Display(Name = "Died on")]
    public string? DiedOn { get; set; }

    [Display(Name = "Died in")]
    public string? DiedPlace { get; set; }

    public string? Notes { get; set; }
}
