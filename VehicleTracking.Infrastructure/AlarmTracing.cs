using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace VehicleTracking.Infrastructure;

[Table("AlarmTracing")]
public partial class AlarmTracing
{
    [Key]
    public int Id { get; set; }

    public int AlarmId { get; set; }

    public int AlarmState { get; set; }

    public int AlarmProtocol { get; set; }

    public int UserId { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime Created { get; set; }

    [StringLength(512)]
    [Unicode(false)]
    public string Description { get; set; } = null!;
}
