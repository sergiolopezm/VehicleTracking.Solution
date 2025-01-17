using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace VehicleTracking.Infrastructure;

[Table("Vehicle")]
public partial class Vehicle
{
    [Key]
    public int Id { get; set; }

    public DateOnly Created { get; set; }

    [StringLength(10)]
    [Unicode(false)]
    public string Patent { get; set; } = null!;

    [StringLength(10)]
    [Unicode(false)]
    public string Economic { get; set; } = null!;

    [StringLength(15)]
    [Unicode(false)]
    public string Brand { get; set; } = null!;

    [StringLength(15)]
    [Unicode(false)]
    public string Model { get; set; } = null!;

    [StringLength(25)]
    [Unicode(false)]
    public string KindOf { get; set; } = null!;

    public int Weight { get; set; }

    public int Capacity { get; set; }

    [StringLength(150)]
    [Unicode(false)]
    public string Provider { get; set; } = null!;

    [StringLength(100)]
    [Unicode(false)]
    public string User { get; set; } = null!;

    [StringLength(60)]
    [Unicode(false)]
    public string Password { get; set; } = null!;

    [InverseProperty("Vehicle")]
    public virtual ICollection<VehicleInfoLocation> VehicleInfoLocations { get; set; } = new List<VehicleInfoLocation>();
}
