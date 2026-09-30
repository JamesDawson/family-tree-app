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

    // Only used when adding a new person who is related to an existing one (Create form).
    // Relation is one of: parent, child, sibling, spouse.
    public string? Relation { get; set; }

    public string? RelatedToId { get; set; }

    [Display(Name = "Other parent")]
    public string? SecondParentId { get; set; }

    [Display(Name = "Married on")]
    public string? MarriedOn { get; set; }

    [Display(Name = "Divorced on")]
    public string? DivorcedOn { get; set; }

    [Display(Name = "Current spouse")]
    public bool CurrentSpouse { get; set; } = true;
}
