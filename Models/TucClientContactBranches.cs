using System.ComponentModel.DataAnnotations.Schema;

namespace Hub.Models;

// Not a column: the branches this contact is scoped to (tblContactBranch), filled by the repository when it loads a
// contact for login or an agent pass. Empty = no scope = sees every branch.
public partial class TucClientContact
{
    [NotMapped]
    public List<int> BranchIds { get; set; } = [];
}
