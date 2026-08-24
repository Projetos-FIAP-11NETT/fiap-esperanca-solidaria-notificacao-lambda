using FiapEsperancaSolidaria.Notifications.Lambda.Models;

namespace FiapEsperancaSolidaria.Notifications.Lambda.Services
{
    public interface IEmailService
    {
        Task SendAsync(EmailMessage message);
    }
}
