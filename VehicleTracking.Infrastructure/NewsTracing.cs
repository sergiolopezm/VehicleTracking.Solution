using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace VehicleTracking.Infrastructure;

[Table("NewsTracing")]
public partial class NewsTracing
{
    [Key]
    public int Id { get; set; }

    public int NewsId { get; set; }

    public int NewsState { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime Created { get; set; }

    [StringLength(512)]
    [Unicode(false)]
    public string Description { get; set; } = null!;

    public int UserId { get; set; }
}
