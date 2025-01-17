using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace VehicleTracking.Infrastructure;

[Keyless]
public partial class DataBi
{
    [Column("MANIFIESTO")]
    [StringLength(20)]
    [Unicode(false)]
    public string Manifiesto { get; set; } = null!;

    [Column("REMESA")]
    [StringLength(20)]
    [Unicode(false)]
    public string Remesa { get; set; } = null!;

    [Column("DESTINATARIO")]
    [StringLength(160)]
    [Unicode(false)]
    public string Destinatario { get; set; } = null!;

    [Column("DIRECCION")]
    [StringLength(100)]
    [Unicode(false)]
    public string Direccion { get; set; } = null!;

    [Column("NIT")]
    [StringLength(20)]
    [Unicode(false)]
    public string Nit { get; set; } = null!;

    [Column("CLIENTE")]
    [StringLength(150)]
    [Unicode(false)]
    public string Cliente { get; set; } = null!;

    [Column("DANE_ORIGEN")]
    [StringLength(1)]
    [Unicode(false)]
    public string DaneOrigen { get; set; } = null!;

    [Column("DANE_DESTINO")]
    [StringLength(1)]
    [Unicode(false)]
    public string DaneDestino { get; set; } = null!;

    [Column("PLACA")]
    [StringLength(10)]
    [Unicode(false)]
    public string Placa { get; set; } = null!;

    [Column("TIPO_VEHICULO")]
    [StringLength(25)]
    [Unicode(false)]
    public string TipoVehiculo { get; set; } = null!;

    [Column("CARROCERIA")]
    [StringLength(10)]
    [Unicode(false)]
    public string Carroceria { get; set; } = null!;

    [Column("FECHA_REMESA")]
    [StringLength(4000)]
    public string? FechaRemesa { get; set; }

    [Column("FECHA_LLEGADA")]
    [StringLength(4000)]
    public string? FechaLlegada { get; set; }

    [Column("FECHA_CITA")]
    [StringLength(4000)]
    public string? FechaCita { get; set; }

    [Column("PROCESS")]
    [StringLength(20)]
    [Unicode(false)]
    public string Process { get; set; } = null!;

    [Column("NOVEDAD")]
    [StringLength(50)]
    [Unicode(false)]
    public string? Novedad { get; set; }

    [Column("RESPONSABLE")]
    [StringLength(50)]
    [Unicode(false)]
    public string? Responsable { get; set; }

    [Column("AFECTACION")]
    [StringLength(1)]
    [Unicode(false)]
    public string Afectacion { get; set; } = null!;
}
