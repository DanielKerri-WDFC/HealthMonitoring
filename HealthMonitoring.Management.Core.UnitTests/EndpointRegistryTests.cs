﻿using System;
using System.Linq;
using HealthMonitoring.Management.Core.Registers;
using HealthMonitoring.Management.Core.Repositories;
using HealthMonitoring.Model;
using HealthMonitoring.TestUtils;
using HealthMonitoring.TimeManagement;
using HealthMonitoring.Security;
using Moq;
using Xunit;

namespace HealthMonitoring.Management.Core.UnitTests
{
    public class EndpointRegistryTests
    {
        private readonly EndpointRegistry _registry;
        private readonly Mock<IHealthMonitorTypeRegistry> _healthMonitorTypeRegistry;
        private readonly Mock<IEndpointConfigurationRepository> _configurationStore;
        private readonly Mock<IEndpointStatsManager> _statsManager = new Mock<IEndpointStatsManager>();
        private readonly Mock<ITimeCoordinator> _timeCoordinator = TimeCoordinatorMock.Get();

        public EndpointRegistryTests()
        {
            _healthMonitorTypeRegistry = new Mock<IHealthMonitorTypeRegistry>();
            _configurationStore = new Mock<IEndpointConfigurationRepository>();
            _registry = new EndpointRegistry(_healthMonitorTypeRegistry.Object, _configurationStore.Object, _statsManager.Object, _timeCoordinator.Object);
        }

        [Fact]
        public void EndpointRegistry_should_load_endpoints_from_repository()
        {
            var endpoint = new Endpoint(_timeCoordinator.Object, new EndpointIdentity(Guid.NewGuid(), "monitor", "address"), new EndpointMetadata("name", "group", new[] { "t1", "t2" }, EndpointMetadata.DefaultMonitorTag, DateTime.UtcNow, DateTime.UtcNow));
            _configurationStore.Setup(s => s.LoadEndpoints()).Returns(new[] { endpoint });

            var registry = new EndpointRegistry(_healthMonitorTypeRegistry.Object, _configurationStore.Object, _statsManager.Object, _timeCoordinator.Object);

            Assert.Same(endpoint, registry.GetById(endpoint.Identity.Id));
        }

        [Fact]
        public void RegisterOrUpdate_should_register_new_endpoint_which_should_be_retrievable_later_by_GetById()
        {
            SetupMonitors("monitor");

            var expectedLastModifiedTime = DateTime.UtcNow;
            _timeCoordinator.Setup(c => c.UtcNow).Returns(expectedLastModifiedTime);

            var endpointId = _registry.RegisterOrUpdate("monitor", "address", "group", "name", new[] { "t1" }, null, "password");
            Assert.NotEqual(Guid.Empty, endpointId);

            var endpoint = _registry.GetById(endpointId);

            Assert.NotNull(endpoint);
            Assert.Equal("monitor", endpoint.Identity.MonitorType);
            Assert.Equal("address", endpoint.Identity.Address);
            Assert.Equal("password".ToSha256Hash(), endpoint.Password);
            Assert.Equal("name", endpoint.Metadata.Name);
            Assert.Equal("group", endpoint.Metadata.Group);
            Assert.Equal(endpointId, endpoint.Identity.Id);
            Assert.Equal("t1", endpoint.Metadata.Tags[0]);
            Assert.Equal(expectedLastModifiedTime, endpoint.LastModifiedTimeUtc);
        }

        [Fact]
        public void RegisterOrUpdate_should_register_new_endpoint_with_default_monitorTag_if_not_specified()
        {
            SetupMonitors("monitor");

            var endpointId = _registry.RegisterOrUpdate("monitor", "address", "group", "name", new[] { "t1" }, null, "password");
            var endpoint = _registry.GetById(endpointId);
            Assert.Equal(EndpointMetadata.DefaultMonitorTag, endpoint.Metadata.MonitorTag);
        }

        [Fact]
        public void RegisterOrUpdate_should_register_new_endpoint_with_custom_monitorTag()
        {
            SetupMonitors("monitor");

            var someMonitorTag = "some_monitor_tag";

            var endpointId = _registry.RegisterOrUpdate("monitor", "address", "group", "name", new[] { "t1" }, someMonitorTag, "password");
            var endpoint = _registry.GetById(endpointId);
            Assert.Equal(someMonitorTag, endpoint.Metadata.MonitorTag);
        }

        [Theory]
        [InlineData(null, "some_tag", "some_tag")]
        [InlineData("some_tag", null, "some_tag")]
        [InlineData(null, null, EndpointMetadata.DefaultMonitorTag)]
        [InlineData("some_tag", "some_tag2", "some_tag2")]
        public void RegisterOrUpdate_should_update_endpoint_monitorTag_but_only_if_not_null(string createdWith, string updatedWith, string expected)
        {
            SetupMonitors("monitor");

            var endpointId = _registry.RegisterOrUpdate("monitor", "address", "group", "name", new[] { "t1" }, createdWith, "password");
            Assert.Equal(endpointId, _registry.RegisterOrUpdate("monitor", "address", "group", "name", new[] { "t1" }, updatedWith, "password"));

            var endpoint = _registry.GetById(endpointId);
            Assert.Equal(expected, endpoint.Metadata.MonitorTag);
        }

        [Fact]
        public void RegisterOrUpdate_shouldnt_update_tags_if_null_passed()
        {
            SetupMonitors("monitor");

            var endpointId = _registry.RegisterOrUpdate("monitor", "address", "group", "name", new[] { "t1" }, null, "password");
            var endpoint = _registry.GetById(endpointId);

            Assert.NotNull(endpoint);
            Assert.Equal("t1", endpoint.Metadata.Tags[0]);

            endpointId = _registry.RegisterOrUpdate("monitor", "address", "group", "name", null, null, "password");
            endpoint = _registry.GetById(endpointId);

            Assert.NotNull(endpoint);
            Assert.Equal("t1", endpoint.Metadata.Tags[0]);
        }

        [Fact]
        public void RegisterOrUpdate_should_not_update_existing_tags_if_null_passed()
        {
            SetupMonitors("monitor");

            var endpointId = _registry.RegisterOrUpdate("monitor", "address", "group", "name", new[] { "t1", "t2" }, null, "password");
            var endpoint = _registry.GetById(endpointId);

            Assert.NotNull(endpoint);

            endpointId = _registry.RegisterOrUpdate("monitor", "address", "group", "name", null, null, "password");
            endpoint = _registry.GetById(endpointId);

            Assert.Equal(new[] { "t1", "t2" }, endpoint.Metadata.Tags);
        }

        [Fact]
        public void RegisterOrUpdate_should_save_new_endpoint_to_repository_when_it_is_created_or_updated()
        {
            SetupMonitors("monitor");

            var id = _registry.RegisterOrUpdate("monitor", "address", "group", "name", new[] { "t1", "t2" }, null, "password");

            _configurationStore.Verify(s => s.SaveEndpoint(It.Is<Endpoint>(e => e.Identity.Id == id)));

            var newName = "name1";
            _registry.RegisterOrUpdate("monitor", "address", "group", newName, new[] { "t1", "t2" }, null, "password");
            _configurationStore.Verify(s => s.SaveEndpoint(It.Is<Endpoint>(e => e.Identity.Id == id && e.Metadata.Name == newName)));
        }

        [Fact]
        public void RegisterOrUpdate_should_register_new_endpoint_if_monitor_and_address_pair_is_different()
        {
            SetupMonitors("monitor", "monitor1");

            var id1 = _registry.RegisterOrUpdate("monitor", "address", "group", "name", new[] { "t1", "t2" }, null, "password");
            var id2 = _registry.RegisterOrUpdate("monitor1", "address", "group", "name", new[] { "t1", "t2" }, null, "password");
            var id3 = _registry.RegisterOrUpdate("monitor", "address1", "group", "name", new[] { "t1", "t2" }, null, "password");

            Assert.NotEqual(id1, id2);
            Assert.NotEqual(id1, id3);
            Assert.NotEqual(id2, id3);
        }

        [Fact]
        public void RegisterOrUpdate_should_update_existing_endpoint_and_return_same_id()
        {
            SetupMonitors("monitor");

            var updateTime1 = DateTime.UtcNow;
            var updateTime2 = DateTime.UtcNow.AddMinutes(1);
            _timeCoordinator.Setup(c => c.UtcNow)
                .Returns(updateTime1);

            var id = _registry.RegisterOrUpdate("monitor", "address", "group", "name", new[] { "t1", "t2" }, null, "password");
            Assert.Equal(updateTime1, _registry.GetById(id).LastModifiedTimeUtc);

            _timeCoordinator.Setup(c => c.UtcNow)
                .Returns(updateTime2);

            var id2 = _registry.RegisterOrUpdate("monitor", "ADDRESS", "group2", "name2", new[] { "t1", "t2" }, null, "password2");

            Assert.Equal(id, id2);

            var endpoint = _registry.GetById(id);
            Assert.NotNull(endpoint);
            Assert.Equal("monitor", endpoint.Identity.MonitorType);
            Assert.Equal("address", endpoint.Identity.Address);
            Assert.Equal("password2".ToSha256Hash(), endpoint.Password);
            Assert.Equal("name2", endpoint.Metadata.Name);
            Assert.Equal("group2", endpoint.Metadata.Group);
            Assert.Equal(updateTime2, endpoint.LastModifiedTimeUtc);
        }

        [Fact]
        public void TryUnregister_should_remove_endpoint_and_dispose_it()
        {
            SetupMonitors("monitor");
            var id = _registry.RegisterOrUpdate("monitor", "address", "group", "name", new[] { "t1", "t2" }, null, "password");
            var endpoint = _registry.GetById(id);
            Assert.True(_registry.TryUnregisterById(id), "Endpoint should be unregistered");
            Assert.True(endpoint.IsDisposed, "Endpoint should be disposed");
            Assert.Null(_registry.GetById(id));

            _configurationStore.Verify(s => s.DeleteEndpoint(id));
        }

        [Fact]
        public void TryUnregister_should_return_false_if_endpoint_is_not_registered()
        {
            Assert.False(_registry.TryUnregisterById(Guid.NewGuid()));
        }

        [Fact]
        public void GetById_should_return_null_for_unknown_id()
        {
            Assert.Null(_registry.GetById(Guid.NewGuid()));
        }

        [Fact]
        public void RegisterOrUpdate_should_throw_UnsupportedMonitorException_if_monitor_is_not_recognized()
        {
            _healthMonitorTypeRegistry.Setup(r => r.GetMonitorTypes()).Returns(new string[0]);
            var exception = Assert.Throws<UnsupportedMonitorException>(() => _registry.RegisterOrUpdate("monitor", "a", "b", "c", new[] { "t1", "t2" }, null, "password"));
            Assert.Equal("Unsupported monitor: monitor", exception.Message);
        }

        [Fact]
        public void Endpoints_should_return_all_endpoints()
        {
            SetupMonitors("monitor");
            var id1 = _registry.RegisterOrUpdate("monitor", "address", "group", "name", null, null, "password");
            var id2 = _registry.RegisterOrUpdate("monitor", "address2", "group", "name", null, null, "password");

            Assert.Equal(new[] { id1, id2 }.OrderBy(i => i).ToArray(), _registry.Endpoints.Select(e => e.Identity.Id).OrderBy(i => i).ToArray());
        }

        [Fact]
        public void UpdateHealth_should_update_health_and_save_it_in_repository()
        {
            SetupMonitors("monitor");
            var id = _registry.RegisterOrUpdate("monitor", "address", "group", "name", null, null, "password");
            var health = new EndpointHealth(DateTime.UtcNow, TimeSpan.Zero, EndpointStatus.Healthy);
            Assert.True(_registry.UpdateHealth(id, health));
            _statsManager.Verify(r => r.RecordEndpointStatistics(It.IsAny<EndpointIdentity>(), It.IsAny<EndpointMetadata>(), health));
            Assert.Same(_registry.GetById(id).Health, health);
        }

        [Fact]
        public void UpdateHealth_should_return_false_for_unknown_endpoints()
        {
            var health = new EndpointHealth(DateTime.UtcNow, TimeSpan.Zero, EndpointStatus.Healthy);
            var id = Guid.NewGuid();
            Assert.False(_registry.UpdateHealth(id, health));
        }

        [Fact]
        public void RegisterOrUpdate_should_trigger_EndpointAdded_event()
        {
            SetupMonitors("monitor");
            Endpoint captured = null;

            _registry.EndpointAdded += e => { captured = e; };
            var id = _registry.RegisterOrUpdate("monitor", "address", "group", "name", new string[0], null, null);
            Assert.NotNull(captured);
            Assert.Same(captured, _registry.GetById(id));
        }

        [Fact]
        public void RegisterOrUpdate_should_not_trigger_EndpointAdded_event_if_endpoint_already_exists()
        {
            SetupMonitors("monitor");
            _registry.RegisterOrUpdate("monitor", "address", "group", "name", new string[0], null, null);

            Endpoint captured = null;
            _registry.EndpointAdded += e => { captured = e; };
            _registry.RegisterOrUpdate("monitor", "address", "group", "name", new string[0], null, null);

            Assert.Null(captured);
        }

        [Fact]
        public void RegisterOrUpdate_should_update_registration_dates()
        {
            SetupMonitors("monitor");

            var firstRegistrationDate = DateTime.UtcNow;
            _timeCoordinator.Setup(c => c.UtcNow).Returns(firstRegistrationDate);

            var endpointId = _registry.RegisterOrUpdate("monitor", "address", "group", "name", new[] { "t1" }, null, "password");
            Assert.NotEqual(Guid.Empty, endpointId);

            var endpoint = _registry.GetById(endpointId);

            Assert.NotNull(endpoint);
            Assert.Equal(firstRegistrationDate, endpoint.Metadata.RegisteredOnUtc);
            Assert.Equal(firstRegistrationDate, endpoint.Metadata.RegistrationUpdatedOnUtc);

            var secondRegistrationDate = DateTime.UtcNow;
            _timeCoordinator.Setup(c => c.UtcNow).Returns(secondRegistrationDate);

            var endpointIdUpdated = _registry.RegisterOrUpdate("monitor", "address", "group", "name", new[] { "t1" }, null, "password");
            Assert.NotEqual(Guid.Empty, endpointId);

            Assert.Equal(endpointId, endpointIdUpdated);

            Assert.NotNull(endpoint);
            Assert.Equal(firstRegistrationDate, endpoint.Metadata.RegisteredOnUtc);
            Assert.Equal(secondRegistrationDate, endpoint.Metadata.RegistrationUpdatedOnUtc);
        }

        private void SetupMonitors(params string[] monitorTypes)
        {
            _healthMonitorTypeRegistry
                .Setup(r => r.GetMonitorTypes())
                .Returns(monitorTypes);
        }
    }
}