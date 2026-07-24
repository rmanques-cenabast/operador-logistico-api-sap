namespace OperadorLogistico.Domain.Enums;

public enum TipoMovimientoSap
{
    // 1. Recepciones y Devoluciones Proveedor
    RecepcionPedidoCompra_101 = 101,
    AnulacionRecepcionPedido_102 = 102,
    DevolucionProveedor_122 = 122,
    AnulacionDevolucionProveedor_123 = 123,
    EntradaConsignacionCalidad_503 = 503,
    AnulacionEntradaConsignacion_504 = 504,

    // 2. Control de Calidad
    TraspasoCalidadALibreDisposicion_321 = 321,
    TraspasoLibreDisposicionACalidad_322 = 322,
    MuestreoLaboratorio_331 = 331,
    AnulacionMuestreoLaboratorio_332 = 332,
    TraspasoCalidadABloqueado_350 = 350,
    TraspasoBloqueadoACalidad_349 = 349,

    // 3. Libre Utilización / Operaciones
    SalidaVentaCliente_601 = 601,
    AnulacionSalidaVenta_602 = 602,
    TraspasoAlmacenExterno_647 = 647,
    AnulacionTraspasoExterno_648 = 648,
    TraspasoLibreABloqueado_344 = 344,
    TraspasoBloqueadoALibre_343 = 343,
    AjusteInventarioSobranteLibre_711 = 711,
    AjusteInventarioFaltanteLibre_712 = 712,

    // 4. Stock Bloqueado / Bajas
    EntradaContramuestraBloqueado_505 = 505,
    SalidaContramuestraBloqueado_506 = 506,
    BajaDesguaceBloqueado_555 = 555,
    AnulacionBajaDesguace_556 = 556,
    AjusteInventarioSobranteBloqueado_717 = 717,
    AjusteInventarioFaltanteBloqueado_718 = 718,

    // 5. Movimientos Internos en Libre Utilización (Traspasos)
    TraspasoCentroACentro_301 = 301,
    AnulacionTraspasoCentro_302 = 302,
    TraspasoMaterialAMaterial_309 = 309,
    AnulacionTraspasoMaterial_310 = 310,
    TraspasoAlmacenAAlmacen_311 = 311,
    AnulacionTraspasoAlmacen_312 = 312,
    TraspasoEspecialAPropio_411 = 411,
    AnulacionTraspasoEspecial_412 = 412
}
