using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace VehicleTracking.Infrastructure;

[Table("Client")]
public partial class Client
{
    [Key]
    public int Id { get; set; }

    public DateOnly Created { get; set; }

    public bool Active { get; set; }

    public int CityId { get; set; }

    public int IdentityTypeId { get; set; }

    [StringLength(20)]
    [Unicode(false)]
    public string Identity { get; set; } = null!;

    [StringLength(1)]
    [Unicode(false)]
    public string Verify { get; set; } = null!;

    [StringLength(150)]
    [Unicode(false)]
    public string Name { get; set; } = null!;

    [StringLength(50)]
    [Unicode(false)]
    public string Phone { get; set; } = null!;

    [StringLength(150)]
    [Unicode(false)]
    public string Address { get; set; } = null!;
}
