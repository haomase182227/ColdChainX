using System.Text.Json;
using ColdChainX.Application.Interfaces;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ColdChainX.Infrastructure.Services.Firebase;

public static class FirebaseServiceCollectionExtensions
{
    private static readonly object InitializationLock = new();
    private static FirebaseApp? _firebaseApp;

    public static IServiceCollection AddFirebaseCloudMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = configuration
            .GetSection(FirebaseOptions.SectionName)
            .Get<FirebaseOptions>() ?? new FirebaseOptions();

        var initialization = TryInitialize(options);
        services.AddSingleton(initialization.Status);

        if (initialization.App != null)
        {
            services.AddSingleton(initialization.App);
            services.AddSingleton(FirebaseMessaging.GetMessaging(initialization.App));
            services.AddSingleton<IFirebaseMessagingClient, FirebaseMessagingClient>();
        }
        else
        {
            services.AddSingleton<IFirebaseMessagingClient>(
                new UnavailableFirebaseMessagingClient(initialization.Status.Error));
        }

        return services;
    }

    private static (FirebaseApp? App, FirebaseConfigurationStatus Status) TryInitialize(
        FirebaseOptions options)
    {
        try
        {
            var credentialSource = ResolveCredential(options);
            if (credentialSource.Credential == null)
            {
                return (null, new FirebaseConfigurationStatus
                {
                    IsConfigured = false,
                    Error = credentialSource.Error,
                    CredentialSource = credentialSource.Source
                });
            }

            lock (InitializationLock)
            {
                _firebaseApp ??= TryGetDefaultApp() ?? FirebaseApp.Create(new AppOptions
                {
                    Credential = credentialSource.Credential,
                    ProjectId = FirstNonEmpty(options.ProjectId, credentialSource.ProjectId)
                });
            }

            return (_firebaseApp, new FirebaseConfigurationStatus
            {
                IsConfigured = true,
                CredentialSource = credentialSource.Source
            });
        }
        catch (Exception ex)
        {
            return (null, new FirebaseConfigurationStatus
            {
                IsConfigured = false,
                Error = $"Firebase Admin SDK initialization failed: {SanitizeInitializationError(ex)}"
            });
        }
    }

    private static CredentialResolution ResolveCredential(FirebaseOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ServiceAccountJson))
        {
            var json = options.ServiceAccountJson.Trim();
            return new CredentialResolution(
                GoogleCredential.FromJson(json),
                TryReadProjectId(json),
                "Firebase:ServiceAccountJson",
                null);
        }

        if (!string.IsNullOrWhiteSpace(options.ServiceAccountPath))
        {
            var configuredPath = options.ServiceAccountPath.Trim();
            var path = Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.GetFullPath(configuredPath, Directory.GetCurrentDirectory());

            if (!File.Exists(path))
            {
                return new CredentialResolution(
                    null,
                    null,
                    "Firebase:ServiceAccountPath",
                    "Firebase service-account file was not found at the configured path.");
            }

            var json = File.ReadAllText(path);
            return new CredentialResolution(
                GoogleCredential.FromJson(json),
                TryReadProjectId(json),
                "Firebase:ServiceAccountPath",
                null);
        }

        if (!string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS")))
        {
            return new CredentialResolution(
                GoogleCredential.GetApplicationDefault(),
                null,
                "GOOGLE_APPLICATION_CREDENTIALS",
                null);
        }

        return new CredentialResolution(
            null,
            null,
            null,
            "Firebase is not configured. Set Firebase:ServiceAccountJson, " +
            "Firebase:ServiceAccountPath, or GOOGLE_APPLICATION_CREDENTIALS.");
    }

    private static FirebaseApp? TryGetDefaultApp()
    {
        try
        {
            return FirebaseApp.DefaultInstance;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string? TryReadProjectId(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("project_id", out var projectId)
                ? projectId.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? FirstNonEmpty(string? first, string? second)
        => !string.IsNullOrWhiteSpace(first)
            ? first.Trim()
            : string.IsNullOrWhiteSpace(second)
                ? null
                : second.Trim();

    private static string SanitizeInitializationError(Exception exception)
        => exception switch
        {
            JsonException => "the service-account JSON is invalid.",
            InvalidOperationException => "the Firebase application configuration is invalid.",
            _ => "credentials could not be loaded."
        };

    private sealed record CredentialResolution(
        GoogleCredential? Credential,
        string? ProjectId,
        string? Source,
        string? Error);
}
