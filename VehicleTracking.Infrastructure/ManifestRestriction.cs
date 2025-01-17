using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace VehicleTracking.Infrastructure;

[Table("ManifestRestriction")]
public partial class ManifestRestriction
{
    [Key]
    public int Id { get; set; }

    public int ManifestId { get; set; }

    public int RestrictionId { get; set; }

    public int Min { get; set; }

    public int Max { get; set; }
}
