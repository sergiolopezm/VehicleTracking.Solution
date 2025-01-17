using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace VehicleTracking.Infrastructure;

public partial class Novelty
{
    [Key]
    public int Id { get; set; }

    public DateTime Created { get; set; }

    public int Process { get; set; }

    public int ResponsibleId { get; set; }

    public bool Active { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string Name { get; set; } = null!;

    [StringLength(250)]
    [Unicode(false)]
    public string Description { get; set; } = null!;
}
