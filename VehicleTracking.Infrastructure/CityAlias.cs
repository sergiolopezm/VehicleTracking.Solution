using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace VehicleTracking.Infrastructure;

[Table("CityAlias")]
public partial class CityAlias
{
    [Key]
    public int Id { get; set; }

    public int CityId { get; set; }

    [StringLength(150)]
    [Unicode(false)]
    public string Alias { get; set; } = null!;
}
