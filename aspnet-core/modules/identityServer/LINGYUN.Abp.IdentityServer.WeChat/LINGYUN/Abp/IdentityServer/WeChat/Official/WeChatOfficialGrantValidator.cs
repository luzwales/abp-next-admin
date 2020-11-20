﻿using IdentityModel;
using IdentityServer4.Events;
using IdentityServer4.Models;
using IdentityServer4.Services;
using IdentityServer4.Validation;
using LINGYUN.Abp.WeChat;
using LINGYUN.Abp.WeChat.Official;
using LINGYUN.Abp.WeChat.OpenId;
using LINGYUN.Abp.WeChat.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using Volo.Abp.Identity;
using Volo.Abp.IdentityServer.Localization;
using Volo.Abp.Security.Claims;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace LINGYUN.Abp.IdentityServer.WeChat.Official
{
    /// <summary>
    /// 对于公众平台绑定用户的扩展授权验证器
    /// </summary>
    public class WeChatOfficialGrantValidator : IExtensionGrantValidator
    {
        protected ILogger<WeChatOfficialGrantValidator> Logger { get; }
        protected AbpWeChatOfficialOptions Options { get; }
        protected IHttpClientFactory HttpClientFactory { get; }
        protected IEventService EventService { get; }
        protected IWeChatOpenIdFinder WeChatOpenIdFinder { get; }
        protected IIdentityUserRepository UserRepository { get; }
        protected UserManager<IdentityUser> UserManager { get; }
        protected SignInManager<IdentityUser> SignInManager { get; }
        protected IStringLocalizer<AbpIdentityServerResource> Localizer { get; }
        protected PhoneNumberTokenProvider<IdentityUser> PhoneNumberTokenProvider { get; }


        public WeChatOfficialGrantValidator(
            IEventService eventService,
            IWeChatOpenIdFinder weChatOpenIdFinder,
            IHttpClientFactory httpClientFactory,
            UserManager<IdentityUser> userManager,
            IIdentityUserRepository userRepository,
            SignInManager<IdentityUser> signInManager,
            IStringLocalizer<AbpIdentityServerResource> stringLocalizer,
            PhoneNumberTokenProvider<IdentityUser> phoneNumberTokenProvider,
            IOptions<AbpWeChatOfficialOptions> options,
            ILogger<WeChatOfficialGrantValidator> logger)
        {
            Logger = logger;
            Options = options.Value;

            EventService = eventService;
            UserManager = userManager;
            SignInManager = signInManager;
            Localizer = stringLocalizer;
            UserRepository = userRepository;
            WeChatOpenIdFinder = weChatOpenIdFinder;
            HttpClientFactory = httpClientFactory;
            PhoneNumberTokenProvider = phoneNumberTokenProvider;
        }

        public string GrantType => AbpWeChatOfficialConsts.GrantType;

        public async Task ValidateAsync(ExtensionGrantValidationContext context)
        {
            var raw = context.Request.Raw;
            var credential = raw.Get(OidcConstants.TokenRequest.GrantType);
            if (credential == null || !credential.Equals(GrantType))
            {
                Logger.LogWarning("Invalid grant type: not allowed");
                context.Result = new GrantValidationResult(TokenRequestErrors.InvalidGrant,
                    Localizer["InvalidGrant:GrantTypeInvalid"]);
                return;
            }
            // TODO: 统一命名规范, 微信认证传递的 code 改为 WeChatOpenIdConsts.WeCahtCodeKey
            var wechatCode = raw.Get(AbpWeChatGlobalConsts.TokenName);
            if (wechatCode.IsNullOrWhiteSpace() || wechatCode.IsNullOrWhiteSpace())
            {
                Logger.LogWarning("Invalid grant type: wechat code not found");
                context.Result = new GrantValidationResult(TokenRequestErrors.InvalidGrant,
                    Localizer["InvalidGrant:WeChatCodeNotFound"]);
                return;
            }
            var wechatOpenId = await WeChatOpenIdFinder.FindAsync(wechatCode, Options.AppId, Options.AppSecret);
            var currentUser = await UserManager.FindByLoginAsync(AbpWeChatOfficialConsts.ProviderKey, wechatOpenId.OpenId);
            if (currentUser == null)
            {
                Logger.LogWarning("Invalid grant type: wechat openid: {0} not register", wechatOpenId.OpenId);
                context.Result = new GrantValidationResult(TokenRequestErrors.InvalidGrant,
                    Localizer["InvalidGrant:WeChatNotRegister"]);
                return;
            }
            var sub = await UserManager.GetUserIdAsync(currentUser);

            var additionalClaims = new List<Claim>();
            if (currentUser.TenantId.HasValue)
            {
                additionalClaims.Add(new Claim(AbpClaimTypes.TenantId, currentUser.TenantId?.ToString()));
            }
            additionalClaims.Add(new Claim(AbpWeChatClaimTypes.OpenId, wechatOpenId.OpenId));
            if (!wechatOpenId.UnionId.IsNullOrWhiteSpace())
            {
                additionalClaims.Add(new Claim(AbpWeChatClaimTypes.UnionId, wechatOpenId.UnionId));
            }

            await EventService.RaiseAsync(new UserLoginSuccessEvent(currentUser.UserName, wechatOpenId.OpenId, null));
            context.Result = new GrantValidationResult(sub,
                AbpWeChatOfficialConsts.AuthenticationMethod, additionalClaims.ToArray());
        }
    }
}
