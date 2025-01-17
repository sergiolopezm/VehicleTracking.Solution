using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace VehicleTracking.Infrastructure;

[Table("BlobStore")]
public partial class BlobStore
{
    [Key]
    public long Id { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime Created { get; set; }

    public int PrimaryId { get; set; }

    public int SecondaryId { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string Container { get; set; } = null!;

    [StringLength(50)]
    [Unicode(false)]
    public string DocType { get; set; } = null!;

    [StringLength(100)]
    [Unicode(false)]
    public string FileName { get; set; } = null!;
}
