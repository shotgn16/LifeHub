namespace LifeHub.Components.Shared;

/// <summary>Kept in C# because the Razor parser doesn't support raw string literals.</summary>
public static class ApiExamples
{
    public static string HomeAssistant(string baseUrl) => $$$"""
        # secrets.yaml
        lifehub_api_key: "<your key>"

        # configuration.yaml
        rest_command:
          lifehub_capture:
            url: "{{{baseUrl}}}/api/v1/capture"
            method: POST
            headers:
              X-Api-Key: !secret lifehub_api_key
            content_type: "application/json"
            payload: '{"text": "{{ text }}"}'
          lifehub_shopping:
            url: "{{{baseUrl}}}/api/v1/shopping"
            method: POST
            headers:
              X-Api-Key: !secret lifehub_api_key
            content_type: "application/json"
            payload: '{"name": "{{ item }}"}'

        # Then in a script, automation or Assist sentence trigger:
        #   action: rest_command.lifehub_shopping
        #   data: { item: "milk" }
        """;
}
