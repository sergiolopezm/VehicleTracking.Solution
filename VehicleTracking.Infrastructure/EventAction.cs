using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace VehicleTracking.Infrastructure;

[Table("EventAction")]
public partial class EventAction
{
    [Key]
    public int Id { get; set; }

    public int EventId { get; set; }

    public bool IsAlarm { get; set; }

    [StringLength(32)]
    [Unicode(false)]
    public string Param { get; set; } = null!;

    public bool Active { get; set; }
}
