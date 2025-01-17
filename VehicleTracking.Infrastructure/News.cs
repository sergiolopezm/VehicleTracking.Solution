using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace VehicleTracking.Infrastructure;

public partial class News
{
    [Key]
    public int Id { get; set; }

    public int NewsState { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime Created { get; set; }

    public int ManifestId { get; set; }

    public int? PickUpId { get; set; }

    public int? DeliveryId { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? Attended { get; set; }
}
