using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace VehicleTracking.Infrastructure;

[Table("Manifest")]
public partial class Manifest
{
    [Key]
    public int Id { get; set; }

    public DateOnly Created { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime Updated { get; set; }

    public bool Active { get; set; }

    [StringLength(20)]
    [Unicode(false)]
    public string Number { get; set; } = null!;

    public int RouteId { get; set; }

    public int VehicleId { get; set; }

    public int DriverId { get; set; }

    [StringLength(20)]
    [Unicode(false)]
    public string Trailer { get; set; } = null!;

    public int Process { get; set; }

    public int State { get; set; }

    [Column(TypeName = "decimal(5, 4)")]
    public decimal Progress { get; set; }

    public int Remaining { get; set; }

    public DateOnly? Ended { get; set; }

    public bool Ds { get; set; }

    public bool Hl { get; set; }

    [InverseProperty("Manifest")]
    public virtual ICollection<VehicleInfoLocation> VehicleInfoLocations { get; set; } = new List<VehicleInfoLocation>();
}
