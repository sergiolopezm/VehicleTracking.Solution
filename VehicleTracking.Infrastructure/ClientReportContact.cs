using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace VehicleTracking.Infrastructure;

[Table("ClientReportContact")]
public partial class ClientReportContact
{
    [Key]
    public int Id { get; set; }

    public int ClientId { get; set; }

    [StringLength(150)]
    [Unicode(false)]
    public string Contact { get; set; } = null!;

    [StringLength(50)]
    [Unicode(false)]
    public string? Frequency { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? Invoice { get; set; }
}
