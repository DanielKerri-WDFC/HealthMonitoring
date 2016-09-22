﻿using System.Net.Http.Headers;
using System.Reflection;
using System.Web.Http;
using Autofac;
using Autofac.Integration.WebApi;
using HealthMonitoring.Forwarders;
using HealthMonitoring.Hosting;
using HealthMonitoring.Management.Core;
using HealthMonitoring.Management.Core.Repositories;
using HealthMonitoring.Persistence;
using HealthMonitoring.SelfHost.Filters;
using Microsoft.Owin.Host.HttpListener;
using Newtonsoft.Json.Converters;
using Owin;
using Swashbuckle.Application;

namespace HealthMonitoring.SelfHost.Configuration
{
    public class Startup
    {
        public void Configuration(IAppBuilder appBuilder)
        {
            var config = new HttpConfiguration();
            ConfigureSerializers(config);
            ConfigureRoutes(config);
            ConfigureSwagger(config);
            ConfigureDependencies(config);
            config.EnableCors();
            appBuilder.UseWebApi(config);
        }

        private static void ConfigureSerializers(HttpConfiguration config)
        {
            config.Formatters.JsonFormatter.SerializerSettings.Converters.Add(new StringEnumConverter { CamelCaseText = true });
            config.Formatters.JsonFormatter.SupportedMediaTypes.Add(new MediaTypeHeaderValue("text/html"));
        }

        private static void ConfigureRoutes(HttpConfiguration config)
        {
            config.MapHttpAttributeRoutes();
            config.Routes.MapHttpRoute("Swagger", "api", null, null, new RedirectHandler(SwaggerDocsConfig.DefaultRootUrlResolver, "swagger/ui/index"));
            config.Filters.Add(new ExceptionFilter());
        }

        private static void ConfigureSwagger(HttpConfiguration config)
        {
            config
                .EnableSwagger(c =>
                {
                    c.SingleApiVersion("v1", "Health Monitoring Service");
                    c.IgnoreObsoleteActions();
                    c.IgnoreObsoleteProperties();
                    c.DescribeAllEnumsAsStrings();
                })
                .EnableSwaggerUi(c =>
                {
                    c.DisableValidator();
                    c.CustomAsset("index", typeof(Startup).Assembly, "HealthMonitoring.SelfHost.Content.Swagger.swagger.html");
                });
        }

        private void ConfigureDependencies(HttpConfiguration config)
        {
            var builder = new ContainerBuilder();
            builder.RegisterAssemblyTypes(typeof(Program).Assembly).Where(t => typeof(ApiController).IsAssignableFrom(t)).AsSelf();
            builder.RegisterAssemblyTypes(typeof(EndpointRegistry).Assembly).AsSelf().AsImplementedInterfaces().SingleInstance();
            builder.RegisterAssemblyTypes(typeof(SqlEndpointConfigurationRepository).Assembly).AsSelf().AsImplementedInterfaces().SingleInstance();

            builder.RegisterInstance<IEndpointMetricsForwarderCoordinator>(
                new EndpointMetricsForwarderCoordinator(PluginDiscovery<IEndpointMetricsForwarder>.DiscoverAllInCurrentFolder("*.Forwarders.*.dll")));

            builder.RegisterInstance<IEndpointStatsRepository>(new EndpointStatsRepository(new MySqlDatabase()))
                .OnActivated(a =>
            {
                var coordinator = a.Context.Resolve<IEndpointMetricsForwarderCoordinator>();
                a.Instance.EndpointStatisticsInserted += coordinator.HandleMetricsForwarding;
            });

            var container = builder.Build();
            config.DependencyResolver = new AutofacWebApiDependencyResolver(container);
        }

        private Assembly[] GetIndirectDependencies()
        {
            return new[]
            {
                typeof (OwinHttpListener).Assembly
            };
        }
    }
}