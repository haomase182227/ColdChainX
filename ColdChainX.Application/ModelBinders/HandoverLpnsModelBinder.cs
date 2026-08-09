using Microsoft.AspNetCore.Mvc.ModelBinding;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs.Delivery;

namespace ColdChainX.Application.ModelBinders;

public class HandoverLpnsModelBinder : IModelBinder
{
    public async Task BindModelAsync(ModelBindingContext bindingContext)
    {
        if (bindingContext == null)
            throw new ArgumentNullException(nameof(bindingContext));

        var valueProviderResult = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (valueProviderResult == ValueProviderResult.None)
        {
            await BindFromFormCollectionAsync(bindingContext);
            return;
        }

        var value = valueProviderResult.FirstValue;
        if (string.IsNullOrEmpty(value))
        {
            await BindFromFormCollectionAsync(bindingContext);
            return;
        }

        try
        {
            var trimmed = value.TrimStart();
            if (trimmed.StartsWith("[") || trimmed.StartsWith("{"))
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                var result = JsonSerializer.Deserialize<List<HandoverConfirmLpnInput>>(value, options);
                bindingContext.Result = ModelBindingResult.Success(result);
                return;
            }
        }
        catch
        {
        }

        await BindFromFormCollectionAsync(bindingContext);
    }

    private Task BindFromFormCollectionAsync(ModelBindingContext bindingContext)
    {
        var request = bindingContext.ActionContext.HttpContext.Request;
        if (!request.HasFormContentType)
        {
            return Task.CompletedTask;
        }

        var form = request.Form;
        var list = new List<HandoverConfirmLpnInput>();

        int index = 0;
        while (true)
        {
            var lpnIdKey = $"{bindingContext.ModelName}[{index}].LpnId";
            var isAcceptedKey = $"{bindingContext.ModelName}[{index}].IsAccepted";

            if (!form.ContainsKey(lpnIdKey) && !form.ContainsKey(isAcceptedKey))
            {
                break;
            }

            var item = new HandoverConfirmLpnInput();

            if (form.TryGetValue(lpnIdKey, out var lpnIdVal) && Guid.TryParse(lpnIdVal, out var lpnId))
            {
                item.LpnId = lpnId;
            }

            if (form.TryGetValue(isAcceptedKey, out var isAcceptedVal) && bool.TryParse(isAcceptedVal, out var isAccepted))
            {
                item.IsAccepted = isAccepted;
            }

            var rejectionReasonKey = $"{bindingContext.ModelName}[{index}].RejectionReason";
            if (form.TryGetValue(rejectionReasonKey, out var rejectionReasonVal))
            {
                item.RejectionReason = rejectionReasonVal;
            }

            var rejectionNotesKey = $"{bindingContext.ModelName}[{index}].RejectionNotes";
            if (form.TryGetValue(rejectionNotesKey, out var rejectionNotesVal))
            {
                item.RejectionNotes = rejectionNotesVal;
            }

            var evidenceImageUrlKey = $"{bindingContext.ModelName}[{index}].EvidenceImageUrl";
            if (form.TryGetValue(evidenceImageUrlKey, out var evidenceImageUrlVal))
            {
                item.EvidenceImageUrl = evidenceImageUrlVal;
            }

            var conditionImageUrlKey = $"{bindingContext.ModelName}[{index}].ConditionImageUrl";
            if (form.TryGetValue(conditionImageUrlKey, out var conditionImageUrlVal))
            {
                item.ConditionImageUrl = conditionImageUrlVal;
            }

            var evidenceFileKey = $"{bindingContext.ModelName}[{index}].EvidencePhotoFile";
            item.EvidencePhotoFile = form.Files.GetFile(evidenceFileKey);

            var conditionFileKey = $"{bindingContext.ModelName}[{index}].ConditionPhotoFile";
            item.ConditionPhotoFile = form.Files.GetFile(conditionFileKey);

            list.Add(item);
            index++;
        }

        if (list.Count > 0)
        {
            bindingContext.Result = ModelBindingResult.Success(list);
        }

        return Task.CompletedTask;
    }
}
