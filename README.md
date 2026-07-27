# Conector SAP - API .NET 🚀

Este proyecto es una API construida en **.NET 8** que sirve como puente de comunicación entre el sistema del Operador Logístico (API Express) y el ERP **SAP**. 

Su única función es recibir los movimientos de inventario en formato JSON, validarlos y enviarlos a SAP utilizando sus BAPIs estándar.

---

## 🗺️ Flujo de la Información (Paso a Paso)

El flujo de los datos viaja de forma lineal y ordenada de la siguiente manera:

```
┌──────────────────┐     ┌─────────────────┐     ┌─────────────────┐     ┌──────────────┐
│  Terminal WMS    │ ──> │   API Express   │ ───> │    API .NET     │ ──> │   ERP SAP    │
│ (Operario OL)    │     │  (SQL Server)   │     │ (Este Proyecto) │     │  (QAS / PRD) │
└──────────────────┘     └─────────────────┘     └─────────────────┘     └──────────────┘
```

1. **Terminal WMS**: El operario en la bodega del OL registra un movimiento físico (recepción, reubicación o merma) en su terminal.
2. **API Express**: Recibe el movimiento, lo guarda en la base de datos SQL Server y un worker automático envía los datos pendientes a esta **API .NET**.
3. **API .NET (Este Proyecto)**: Recibe la petición en formato JSON, realiza la conversión de unidades si corresponde y llama a la función de SAP.
4. **ERP SAP**: Procesa el movimiento, asienta el stock en el inventario y devuelve el número de documento de material oficial como confirmación.

---

## 📦 Movimientos Soportados

### 1. Entrada de Mercancías
* **BAPI**: `BAPI_GOODSMVT_CREATE` (GM_CODE `01`).
* **Conversión de Unidad**: Si el Operador Logístico recibe en unidades (`UN`), la API consulta la Orden de Compra de SAP y calcula automáticamente la conversión a cajas (`CAJ`/`KI`) de forma transparente.

### 2. Traspasos Internos
* **BAPI**: `BAPI_GOODSMVT_CREATE` (GM_CODE `04`).
* **Uso**: Mover mercadería entre almacenes del Centro `6000` (Movimiento `311`). Permite cambiar el estado del stock al mismo tiempo (ej: mover a Bloqueado o Control de Calidad).

### 3. Ajustes de Inventario
* **BAPI**: `BAPI_GOODSMVT_CREATE` (GM_CODE `03` / `05`).
* **Uso**: Reportar bajas/mermas físicas (Movimiento `555`) o diferencias por conteos cíclicos (Movimiento `711`).

---

## 🛠️ Configuración
Los datos de conexión al servidor de SAP y base de datos se configuran de forma externa en el archivo **`appsettings.json`** o en las variables de entorno del servidor **IIS** en el ambiente de desarrollo y producción.
