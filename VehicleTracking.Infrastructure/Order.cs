using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace VehicleTracking.Infrastructure;

[Table("Order")]
public partial class Order
{
    [Key]
    public int Id { get; set; }

    public DateOnly Created { get; set; }

    public int? ManifestId { get; set; }

    [StringLength(20)]
    [Unicode(false)]
    public string Number { get; set; } = null!;

    public int ClientId { get; set; }

    [StringLength(60)]
    [Unicode(false)]
    public string? Client { get; set; }

    [StringLength(160)]
    [Unicode(false)]
    public string Contact { get; set; } = null!;

    [StringLength(20)]
    [Unicode(false)]
    public string Phone { get; set; } = null!;

    [StringLength(60)]
    [Unicode(false)]
    public string Email { get; set; } = null!;

    [StringLength(240)]
    [Unicode(false)]
    public string Address { get; set; } = null!;

    public Geometry? Location { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? QuoteDate { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? Ended { get; set; }
}
