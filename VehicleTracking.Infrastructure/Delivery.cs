using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace VehicleTracking.Infrastructure;

[Table("Delivery")]
public partial class Delivery
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
    public string Recipient { get; set; } = null!;

    [StringLength(20)]
    [Unicode(false)]
    public string Phone { get; set; } = null!;

    [StringLength(60)]
    [Unicode(false)]
    public string Email { get; set; } = null!;

    [StringLength(100)]
    [Unicode(false)]
    public string Address { get; set; } = null!;

    public Geometry? Location { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? Ended { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? QuoteDate { get; set; }

    [StringLength(60)]
    [Unicode(false)]
    public string Container { get; set; } = null!;

    [StringLength(15)]
    [Unicode(false)]
    public string Invoice { get; set; } = null!;
}
