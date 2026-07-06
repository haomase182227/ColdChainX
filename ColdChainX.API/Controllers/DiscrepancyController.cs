using ColdChainX.Application.Features.Discrepancy.Commands;
using ColdChainX.Application.Features.Discrepancy.Queries;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace ColdChainX.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DiscrepancyController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IApplicationDbContext _context;
    private readonly IFileService _fileService;

    public DiscrepancyController(IMediator mediator, IApplicationDbContext context, IFileService fileService)
    {
        _mediator = mediator;
        _context = context;
        _fileService = fileService;
    }

    [HttpGet("debug-columns")]
    public async Task<IActionResult> DebugColumns()
    {
        var conn = _context.Database.GetDbConnection();
        var wasOpen = conn.State == System.Data.ConnectionState.Open;
        if (!wasOpen) await conn.OpenAsync();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT table_name, column_name, data_type FROM information_schema.columns WHERE table_schema = 'public' AND table_name IN ('lpns', 'transport_orders')";
            var list = new List<object>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new
                {
                    TableName = reader.GetString(0),
                    ColumnName = reader.GetString(1),
                    DataType = reader.GetString(2)
                });
            }
            return Ok(list);
        }
        finally
        {
            if (!wasOpen) await conn.CloseAsync();
        }
    }

    [HttpGet("pending")]
    public async Task<IActionResult> GetPendingDiscrepancies([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10)
    {
        var result = await _mediator.Send(new GetPendingDiscrepanciesQuery
        {
            PageNumber = pageNumber,
            PageSize = pageSize
        });
        return Ok(result);
    }

    [HttpGet("{lpnId:guid}")]
    public async Task<IActionResult> GetDiscrepancyDetail(Guid lpnId)
    {
        var result = await _mediator.Send(new GetDiscrepancyDetailQuery(lpnId));
        if (result == null)
            return NotFound(new { Message = "Discrepancy LPN not found" });

        return Ok(result);
    }

    [HttpPost("resolve")]
    public async Task<IActionResult> ResolveDiscrepancy([FromBody] ResolveDiscrepancyCommand command)
    {
        var result = await _mediator.Send(command);
        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }

    [HttpGet("{receiptId}/pdf")]
    public async Task<IActionResult> GetDiscrepancyPdf(Guid receiptId)
    {
        try
        {
            var pdfBytes = await _mediator.Send(new ColdChainX.Application.Features.Discrepancy.Queries.GenerateDiscrepancyPdfQuery(receiptId));
            return File(pdfBytes, "application/pdf", $"BienBanBatThuong_{receiptId.ToString().Substring(0, 8)}.pdf");
        }
        catch (Exception ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }

    [HttpPost("regenerate-all-pdfs")]
    public async Task<IActionResult> RegenerateAllPdfs()
    {
        var receipts = await _context.WarehouseReceipts
            .Include(r => r.Order)
            .Where(r => r.ReferenceDocNo == "DISCREPANCY_HOLD")
            .ToListAsync();

        int count = 0;
        foreach (var receipt in receipts)
        {
            try
            {
                var pdfBytes = await _mediator.Send(new GenerateDiscrepancyPdfQuery(receipt.ReceiptId));
                var pdfFileName = $"discrepancy-{receipt.Order?.TrackingCode ?? receipt.ReceiptId.ToString()}-{DateTime.UtcNow:yyyyMMddHHmmss}.pdf";
                var pdfUrl = await _fileService.UploadFileAsync(pdfBytes, pdfFileName);
                
                receipt.PdfUrl = pdfUrl;
                count++;

                // Create or update TransportDocument for discrepancy report
                var doc = await _context.TransportDocuments
                    .FirstOrDefaultAsync(d => d.OrderId == receipt.OrderId && d.DocType == "DISCREPANCY_REPORT");
                if (doc == null)
                {
                    _context.TransportDocuments.Add(new TransportDocument
                    {
                        DocId = Guid.NewGuid(),
                        OrderId = receipt.OrderId,
                        DocType = "DISCREPANCY_REPORT",
                        ImageUrl = pdfUrl,
                        Status = "PENDING",
                        UploadedBy = receipt.ReceiverId,
                        CreatedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    doc.ImageUrl = pdfUrl;
                    doc.Status = "PENDING";
                    doc.CreatedAt = DateTime.UtcNow;
                }

                // Load the appendix for this order to inject the raw HTML
                var appendix = await _context.ContractAppendices
                    .Where(a => a.OrderId == receipt.OrderId)
                    .OrderByDescending(a => a.CreatedAt)
                    .FirstOrDefaultAsync();

                // Update corresponding notifications' Params
                var notifications = await _context.Notifications
                    .Where(n => n.OrderId == receipt.OrderId && n.TemplateId == "NOTI_QC_DISCREPANCY")
                    .ToListAsync();

                foreach (var noti in notifications)
                {
                    if (!string.IsNullOrWhiteSpace(noti.Params))
                    {
                        try
                        {
                            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(noti.Params);
                            if (dict != null)
                            {
                                dict["Pdf_URL"] = pdfUrl;
                                if (appendix != null)
                                {
                                    dict["Appendix_Id"] = appendix.AppendixId.ToString();
                                    dict["Appendix_Number"] = appendix.AppendixNumber;
                                }
                                dict.Remove("Appendix_Raw_Html");
                                noti.Params = JsonSerializer.Serialize(dict);
                            }
                        }
                        catch (Exception)
                        {
                            // Ignore json parsing errors
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Ignore and proceed
            }
        }

        if (count > 0)
        {
            await _context.SaveChangesAsync();
        }

        return Ok(new { Success = true, Message = $"Successfully regenerated {count} discrepancy PDFs and updated notification parameters.", Count = count });
    }
}
