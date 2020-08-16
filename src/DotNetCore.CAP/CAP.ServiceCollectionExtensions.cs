// Copyright (c) .NET Core Community. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using DotNetCore.CAP;
using DotNetCore.CAP.Abstractions;
using DotNetCore.CAP.Internal;
using DotNetCore.CAP.Processor;
using DotNetCore.CAP.Processor.States;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Contains extension methods to <see cref="IServiceCollection" /> for configuring consistence services.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Adds and configures the consistence services for the consistency.
        /// </summary>
        /// <param name="services">The services available in the application.</param>
        /// <param name="setupAction">An action to configure the <see cref="CapOptions" />.</param>
        /// <returns>An <see cref="CapBuilder" /> for application services.</returns>
        public static CapBuilder AddCap(this IServiceCollection services, Action<CapOptions> setupAction)
        {
            if (setupAction == null)
            {
                throw new ArgumentNullException(nameof(setupAction));
            }

            services.TryAddSingleton<CapMarkerService>();

            // 注入消费服务 实现了接口 ICapSubscribe    为下面的defaultSubscriberExecutor选择合适的excutor提供元数据
            AddSubscribeServices(services);

            //Serializer and model binder
            //序列化 消息体
            services.TryAddSingleton<IContentSerializer, JsonContentSerializer>();
            services.TryAddSingleton<IMessagePacker, DefaultMessagePacker>();

            //选择合适的消费者
            services.TryAddSingleton<IConsumerServiceSelector, DefaultConsumerServiceSelector>();
            services.TryAddSingleton<IModelBinderFactory, ModelBinderFactory>();
            services.TryAddSingleton<IConsumerInvokerFactory, ConsumerInvokerFactory>();

            //发送消息后的回调
            services.TryAddSingleton<ICallbackMessageSender, CallbackMessageSender>();

            //缓存消费者执行的方法
            services.TryAddSingleton<MethodMatcherCache>();

            //处理器  hostService启动后 初始化 MQ的监听，Cap自己的逻辑 （处理需要重试的消息和需要删除过期的消息）  
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IProcessingServer, ConsumerHandler>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IProcessingServer, CapProcessingServer>());
            services.TryAddSingleton<IStateChanger, StateChanger>();

            //队列的消息处理器
            //Queue's message processor
            services.TryAddSingleton<NeedRetryMessageProcessor>();

            //单列  发送的消息和接受的消息的处理的缓冲区 先放到dispater中 最后统一处理 多线程处理        Sender and Executors   
            services.TryAddSingleton<IDispatcher, Dispatcher>();

            //默认的消息处理器
            // Warning: IPublishMessageSender need to inject at extension project. 
            services.TryAddSingleton<ISubscriberExecutor, DefaultSubscriberExecutor>();

            //初始化mq 和db的配置
            //Options and extension service
            var options = new CapOptions();
            setupAction(options);
            foreach (var serviceExtension in options.Extensions)
            {
                serviceExtension.AddServices(services);
            }

            //注入配置  给依赖的服务调用
            services.AddSingleton(options);
            
            //配置hostServer服务
            //Startup and Middleware
            services.AddTransient<IHostedService, DefaultBootstrapper>();

            //启动consul的dashboard 后启动业务的中注册的中间件 比如mvc中间件
            services.AddTransient<IStartupFilter, CapStartupFilter>();

            return new CapBuilder(services);
        }

        private static void AddSubscribeServices(IServiceCollection services)
        {
            var consumerListenerServices = new List<KeyValuePair<Type, Type>>();
            foreach (var rejectedServices in services)
            {
                if (rejectedServices.ImplementationType != null
                    && typeof(ICapSubscribe).IsAssignableFrom(rejectedServices.ImplementationType))
                {
                    consumerListenerServices.Add(new KeyValuePair<Type, Type>(typeof(ICapSubscribe),
                        rejectedServices.ImplementationType));
                }
            }

            foreach (var service in consumerListenerServices)
            {
                services.TryAddEnumerable(ServiceDescriptor.Transient(service.Key, service.Value));
            }
        }
    }
}