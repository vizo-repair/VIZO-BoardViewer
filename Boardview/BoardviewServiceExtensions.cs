namespace VIZO.BoardViewer.Boardview;

using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using VIZO.BoardViewer.Boardview.ParseBoardview;
using VIZO.BoardViewer.Boardview.ParseBoardview.Formats;
using VIZO.BoardViewer.Boardview.ParseBoardview.Services;

public static class BoardviewServiceExtensions
{
    public static IServiceCollection AddBoardviewViewer(this IServiceCollection services)
    {
        services.AddSingleton<ComponentRoleDeducer>();
        services.AddSingleton<BoardviewPrefixCatalog>(_ => BoardviewPrefixCatalog.Default);
        services.AddSingleton<QuadrantAnchorScorer>();
        services.AddSingleton<NetlistSanitizer>();

        services.AddSingleton<IBoardviewFormatParser, CompalBdvParser>();
        services.AddSingleton<IBoardviewFormatParser, XzzPcbParser>();
        services.AddSingleton<IBoardviewFormatParser, TvwPcbParser>();
        services.AddSingleton<IBoardviewFormatParser, FzPcbParser>();
        services.AddSingleton<IBoardviewFormatParser, BrdPcbParser>();
        services.AddSingleton<IBoardviewFormatParser, Bvr3PcbParser>();
        services.AddSingleton<IBoardviewFormatParser, BvrPcbParser>();
        services.AddSingleton<IBoardviewFormatParser, Brd2PcbParser>();
        services.AddSingleton<IBoardviewFormatParser, CstPcbParser>();
        services.AddSingleton<IBoardviewFormatParser, GenCadAsciiParser>();
        services.AddSingleton<IBoardviewFormatParser, CadPcbParser>();
        services.AddSingleton<IBoardviewFormatParser, EagleXmlParser>();
        services.AddSingleton<GenCadAsciiParser>();

        services.AddSingleton<BoardviewFormatDetector>();
        services.AddSingleton<BoardviewParseGate>();

        return services;
    }

    public static void MapBoardviewViewerEndpoints(this IEndpointRouteBuilder app)
    {
        ParseBoardviewEndpoint.Map(app);
    }
}
