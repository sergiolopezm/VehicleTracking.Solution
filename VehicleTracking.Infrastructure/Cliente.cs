using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace VehicleTracking.Infrastructure;

[Keyless]
[Table("CLIENTES$")]
public partial class Cliente
{
    [Column("Nit O Cc")]
    [StringLength(255)]
    public string? NitOCc { get; set; }

    [StringLength(255)]
    public string? Div { get; set; }

    [Column("Tipo Documento")]
    [StringLength(255)]
    public string? TipoDocumento { get; set; }

    [Column("Cliente")]
    [StringLength(255)]
    public string? Cliente1 { get; set; }

    [StringLength(255)]
    public string? Estado { get; set; }

    [StringLength(255)]
    public string? Correo { get; set; }

    [StringLength(255)]
    public string? Ciudad { get; set; }

    [StringLength(255)]
    public string? Direccion { get; set; }

    [Column("Telefono 1")]
    [StringLength(255)]
    public string? Telefono1 { get; set; }

    [Column("Telefono 2")]
    [StringLength(255)]
    public string? Telefono2 { get; set; }

    [StringLength(255)]
    public string? Celular { get; set; }

    [Column("Cupo Credito")]
    [StringLength(255)]
    public string? CupoCredito { get; set; }

    [Column("Descripcion Actividad")]
    [StringLength(255)]
    public string? DescripcionActividad { get; set; }

    [Column("Contacto Principal")]
    [StringLength(255)]
    public string? ContactoPrincipal { get; set; }

    [Column("Contacto Logistico")]
    [StringLength(255)]
    public string? ContactoLogistico { get; set; }

    [Column("Contacto Seguridad")]
    [StringLength(255)]
    public string? ContactoSeguridad { get; set; }

    [Column("Contacto Comercial")]
    [StringLength(255)]
    public string? ContactoComercial { get; set; }

    [Column("Contacto Adminis#")]
    [StringLength(255)]
    public string? ContactoAdminis { get; set; }

    [StringLength(255)]
    public string? Obervaciones { get; set; }

    [Column("Obs Estado")]
    [StringLength(255)]
    public string? ObsEstado { get; set; }

    [StringLength(255)]
    public string? Regimen { get; set; }

    [Column("Sede Principal")]
    [StringLength(255)]
    public string? SedePrincipal { get; set; }

    [Column("Contacto Sede")]
    [StringLength(255)]
    public string? ContactoSede { get; set; }

    [Column("Tel Sede")]
    [StringLength(255)]
    public string? TelSede { get; set; }

    [Column("Fax Sede")]
    [StringLength(255)]
    public string? FaxSede { get; set; }

    [Column("Email Sede")]
    [StringLength(255)]
    public string? EmailSede { get; set; }

    [Column("Condiciones De Pago")]
    [StringLength(255)]
    public string? CondicionesDePago { get; set; }

    [Column("Condiciones Fact#")]
    [StringLength(255)]
    public string? CondicionesFact { get; set; }

    [StringLength(255)]
    public string? Instrucciones { get; set; }

    [Column("Dia Pago")]
    [StringLength(255)]
    public string? DiaPago { get; set; }

    [Column("Dia Informacion")]
    [StringLength(255)]
    public string? DiaInformacion { get; set; }

    [Column("Creado Por")]
    [StringLength(255)]
    public string? CreadoPor { get; set; }

    [Column("Fecha Creacion")]
    [StringLength(255)]
    public string? FechaCreacion { get; set; }

    [Column("Modificado Por")]
    [StringLength(255)]
    public string? ModificadoPor { get; set; }

    [Column("Fecha Modificacion")]
    [StringLength(255)]
    public string? FechaModificacion { get; set; }
}
