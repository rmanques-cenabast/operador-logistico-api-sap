using Microsoft.Extensions.Logging;
using OperadorLogistico.Application.DTOs.Documentos;
using OperadorLogistico.Application.Interfaces;
using SAP.Middleware.Connector;

namespace OperadorLogistico.Infrastructure.Sap.Services;

public class SapAnexoService : ISapAnexoService
{
    private readonly ISapConnectionFactory _connectionFactory;
    private readonly ILogger<SapAnexoService> _logger;

    public SapAnexoService(ISapConnectionFactory connectionFactory, ILogger<SapAnexoService> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<AdjuntarDocumentoResponseDto> AdjuntarADocumentoMaterialAsync(AdjuntarDocumentoMaterialDto request)
    {
        return await Task.Run(() =>
        {
            var response = new AdjuntarDocumentoResponseDto();

            try
            {
                if (string.IsNullOrWhiteSpace(request.NumeroDocumentoMaterial))
                {
                    response.Exitoso = false;
                    response.Mensaje = "El número de documento de material no puede estar vacío.";
                    return response;
                }

                if (string.IsNullOrWhiteSpace(request.ContenidoBase64))
                {
                    response.Exitoso = false;
                    response.Mensaje = "El contenido del archivo en Base64 no puede estar vacío.";
                    return response;
                }

                byte[] fileBytes;
                try
                {
                    fileBytes = Convert.FromBase64String(request.ContenidoBase64);
                }
                catch (FormatException ex)
                {
                    _logger.LogError(ex, "Error al decodificar contenido Base64 para el documento {NombreArchivo}", request.NombreArchivo);
                    response.Exitoso = false;
                    response.Mensaje = "El contenido del archivo no posee una codificación Base64 válida.";
                    return response;
                }

                var destination = _connectionFactory.GetDestination();
                RfcSessionManager.BeginContext(destination);

                try
                {
                    var repository = destination.Repository;

                    // 1. Obtener la carpeta raíz compartida de SAPoffice (GOS)
                    IRfcFunction funcFolder = repository.CreateFunction("SO_FOLDER_ROOT_ID_GET");
                    funcFolder.SetValue("REGION", "B"); // B = Shared / Carpetas generales
                    funcFolder.Invoke(destination);
                    IRfcStructure folderId = funcFolder.GetStructure("FOLDER_ID");

                    string folTp = folderId.GetString("OBJTP");
                    string folYr = folderId.GetString("OBJYR");
                    string folNo = folderId.GetString("OBJNO");
                    string folderId17 = $"{folTp}{folYr}{folNo}";

                    // 2. Preparar parámetros de cabecera para SO_DOCUMENT_INSERT_API1
                    IRfcFunction funcInsert = repository.CreateFunction("SO_DOCUMENT_INSERT_API1");
                    funcInsert.SetValue("FOLDER_ID", folderId17);

                    // Determinar tipo de documento
                    string ext = Path.GetExtension(request.NombreArchivo)?.TrimStart('.').ToUpperInvariant() ?? "PDF";
                    string docType = !string.IsNullOrEmpty(request.TipoDocumento) 
                        ? request.TipoDocumento.ToUpperInvariant() 
                        : (ext.Length <= 3 ? ext : "EXT");

                    funcInsert.SetValue("DOCUMENT_TYPE", docType);

                    // Estructura DOCUMENT_DATA (SODOCDAT1)
                    IRfcStructure docData = funcInsert.GetStructure("DOCUMENT_DATA");
                    string nombreLimpio = Path.GetFileNameWithoutExtension(request.NombreArchivo);
                    if (nombreLimpio.Length > 12) nombreLimpio = nombreLimpio.Substring(0, 12);
                    
                    docData.SetValue("OBJ_NAME", string.IsNullOrWhiteSpace(nombreLimpio) ? "ANEXO" : nombreLimpio.ToUpperInvariant());
                    
                    string descripcion = request.NombreArchivo;
                    if (descripcion.Length > 50) descripcion = descripcion.Substring(0, 50);
                    docData.SetValue("OBJ_DESCR", descripcion);
                    docData.SetValue("OBJ_LANGU", "S");
                    docData.SetValue("DOC_SIZE", fileBytes.Length);

                    // 2. Llenar tabla CONTENTS_HEX (SOLIX con bloques de 255 bytes)
                    IRfcTable contentsHex = funcInsert.GetTable("CONTENTS_HEX");
                    int offset = 0;
                    int totalLength = fileBytes.Length;

                    while (offset < totalLength)
                    {
                        int chunkSize = Math.Min(255, totalLength - offset);
                        byte[] chunk = new byte[255];
                        Buffer.BlockCopy(fileBytes, offset, chunk, 0, chunkSize);

                        contentsHex.Append();
                        contentsHex.SetValue("LINE", chunk);
                        offset += chunkSize;
                    }

                    _logger.LogInformation("Invocando SO_DOCUMENT_INSERT_API1 para archivo {NombreArchivo} ({Bytes} bytes en {Chunks} paquetes)",
                        request.NombreArchivo, fileBytes.Length, contentsHex.RowCount);

                    funcInsert.Invoke(destination);

                    // 3. Obtener el DOC_ID del documento creado en SAP
                    IRfcStructure docInfo = funcInsert.GetStructure("DOCUMENT_INFO");
                    string docId = docInfo.GetString("DOC_ID");

                    if (string.IsNullOrWhiteSpace(docId))
                    {
                        _logger.LogError("SO_DOCUMENT_INSERT_API1 no devolvió un DOC_ID válido.");
                        response.Exitoso = false;
                        response.Mensaje = "SAP no generó un identificador de documento válido en SAPoffice.";
                        return response;
                    }

                    _logger.LogInformation("Documento SAPoffice creado exitosamente. DOC_ID: {DocId}", docId);

                    // 4. Vincular el documento al Objeto de Negocio Documento de Material (BUS2017)
                    string docMaterialPadded = request.NumeroDocumentoMaterial.Trim().PadLeft(10, '0');
                    string anio = string.IsNullOrWhiteSpace(request.Ejercicio) 
                        ? DateTime.Now.Year.ToString() 
                        : request.Ejercicio.Trim();
                    string objKeyMaterial = $"{docMaterialPadded}{anio}";

                    IRfcFunction funcRelation = repository.CreateFunction("BINARY_RELATION_CREATE_COMMIT");

                    IRfcStructure objRoleA = funcRelation.GetStructure("OBJ_ROLEA");
                    objRoleA.SetValue("OBJTYPE", "BUS2017");
                    objRoleA.SetValue("OBJKEY", objKeyMaterial);
                    objRoleA.SetValue("LOGSYS", string.Empty);

                    IRfcStructure objRoleB = funcRelation.GetStructure("OBJ_ROLEB");
                    objRoleB.SetValue("OBJTYPE", "MESSAGE");
                    objRoleB.SetValue("OBJKEY", docId);
                    objRoleB.SetValue("LOGSYS", string.Empty);

                    funcRelation.SetValue("RELATIONTYPE", "ATTA"); // Attachment

                    _logger.LogInformation("Vinculando anexo en SAP GOS: BUS2017 Key={ObjKey} con MESSAGE Key={DocId}",
                        objKeyMaterial, docId);

                    funcRelation.Invoke(destination);

                    // Confirmar transacción en SAP
                    try
                    {
                        IRfcFunction bapiCommit = repository.CreateFunction("BAPI_TRANSACTION_COMMIT");
                        bapiCommit.SetValue("WAIT", "X");
                        bapiCommit.Invoke(destination);
                    }
                    catch (Exception exCommit)
                    {
                        _logger.LogWarning(exCommit, "Aviso al ejecutar BAPI_TRANSACTION_COMMIT para anexo");
                    }

                    response.Exitoso = true;
                    response.Mensaje = $"Archivo '{request.NombreArchivo}' adjuntado exitosamente al Documento de Material {request.NumeroDocumentoMaterial} en SAP.";
                    response.DocumentoIdSAP = docId;
                    response.ObjetoClaveSAP = objKeyMaterial;

                    _logger.LogInformation("Anexo vinculado con éxito a Documento de Material {DocMat}", request.NumeroDocumentoMaterial);
                    return response;
                }
                finally
                {
                    RfcSessionManager.EndContext(destination);
                }
            }
            catch (RfcAbapException abapEx)
            {
                _logger.LogError(abapEx, "Error ABAP al adjuntar documento en SAP: {Key} - {Msg}", abapEx.Key, abapEx.Message);
                response.Exitoso = false;
                response.Mensaje = $"Error ABAP al adjuntar en SAP: [{abapEx.Key}] {abapEx.Message}";
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Excepción inesperada al adjuntar documento en SAP");
                response.Exitoso = false;
                response.Mensaje = $"Error de comunicación con SAP: {ex.Message}";
                return response;
            }
        });
    }
}
