﻿using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SADA.Core.Interfaces;
using SADA.Core.Models;
using SADA.Core.ViewModels;
using SADA.Service;
using SADA.Service.Interfaces;
using Stripe.Checkout;
using System.Security.Claims;

namespace SADA.Web.Areas.Client.Controllers
{
    [Area("Client")]
    [Authorize]
    public class CartController : Controller
    {
        private readonly IUnitOfWork _unitOfWorks;
        private readonly ISmsSender  _SmsSender;

        public CartController(IUnitOfWork unitOfWork, ISmsSender smsSender)
        {
            _unitOfWorks = unitOfWork;
            _SmsSender   = smsSender;
        }

        [BindProperty] //for post method
        public ShoppingCartVM ShoppingCartVM { get; set; }

        [HttpGet]
        public IActionResult Index()
        {
            //find logged user to get his cart
            var userId = HttpContext.Session.GetObject<ApplicationUser>(SD.SessionLoggedUser).Id;
            //get cart
            ShoppingCartVM = new()
            {
                ListCart = _unitOfWorks.ShoppingCart.GetAll(
                includeProperties: "Product",
                criteria: c => c.ApplicationUserID == userId
                ),
                OrderHeader = new()
            };
            //calculate total
            foreach( var item in ShoppingCartVM.ListCart)
            {
                ShoppingCartVM.OrderHeader.OrderTotal += (item.Product.Price * item.Count);
            }

            return View(ShoppingCartVM);
        }

        [HttpGet]
        public IActionResult Summary()
        {
            var userId = HttpContext.Session.GetObject<ApplicationUser>(SD.SessionLoggedUser).Id;

            //get cart
            ShoppingCartVM = new()
            {
                ListCart = _unitOfWorks.ShoppingCart.GetAll(
                includeProperties: "Product",
                criteria: c => c.ApplicationUserID == userId
                ),
                OrderHeader = new()
                {
                    //Initialise order header
                    ApplicationUserId = userId,
                    ApplicationUser = _unitOfWorks.ApplicationUser.GetFirstOrDefault(u => u.Id == userId),
                }
            };
            ShoppingCartVM.OrderHeader.Name = ShoppingCartVM.OrderHeader.ApplicationUser.Name;
            ShoppingCartVM.OrderHeader.PhoneNumber = ShoppingCartVM.OrderHeader.ApplicationUser.PhoneNumber;
            ShoppingCartVM.OrderHeader.StreetAddress = ShoppingCartVM.OrderHeader.ApplicationUser.StreetAddress;
            ShoppingCartVM.OrderHeader.City = ShoppingCartVM.OrderHeader.ApplicationUser.City;

            foreach (var item in ShoppingCartVM.ListCart)
            {
                ShoppingCartVM.OrderHeader.OrderTotal += (item.Product.Price * item.Count);
            }

            return View(ShoppingCartVM);
        }

        [HttpPost]
        [ActionName("Summary")]
        [ValidateAntiForgeryToken]
        public IActionResult SummaryPost()
        {
            //OrderHeader
            ShoppingCartVM.OrderHeader.OrderDate = DateTime.Now;
            ShoppingCartVM.OrderHeader.OrderStatus = SD.Status.Pending.ToString();
            //ShoppingCartVM.OrderHeader.PaymentOption = SD.PaymentOptions.Cash;
            ShoppingCartVM.OrderHeader.PaymentStatus = SD.Status.Pending.ToString();
            _unitOfWorks.OrderHeader.Add(ShoppingCartVM.OrderHeader);
            _unitOfWorks.Save();

            //OrderDetail
            ShoppingCartVM.ListCart = _unitOfWorks.ShoppingCart.GetAll(
                includeProperties: "Product",
                criteria: c => c.ApplicationUserID == ShoppingCartVM.OrderHeader.ApplicationUserId
                );
            foreach (var item in ShoppingCartVM.ListCart)
            {
                OrderDetail orderDetail = new()
                {
                    OrderId = ShoppingCartVM.OrderHeader.Id,
                    ProductId = item.ProductID,
                    Count = item.Count,
                    Price = item.Product.Price
                };
                _unitOfWorks.OrderDetail.Add(orderDetail);
                _unitOfWorks.Save();
            }

            //stripe setting
            var domain = "https://localhost:44344/";
            var options = new SessionCreateOptions
            {
                PaymentMethodTypes = new List<string>{ 
                    //(SD.PaymentOptions)SD.PaymentOptions.Visa.ToString()
                    "card",    
                },
                LineItems = new List<SessionLineItemOptions>(),
                Mode = "payment",
                SuccessUrl = domain+$"client/cart/OrderConfirmation?id={ShoppingCartVM.OrderHeader.Id}",
                CancelUrl  = domain+"client/cart/index",
            };

            foreach (var item in ShoppingCartVM.ListCart)
            {
                var sessionLineItem = new SessionLineItemOptions
                {
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        UnitAmount = (long)item.Product.Price * 100,
                        Currency = "egp",
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = item.Product.Name,
                        },

                    },
                    Quantity = item.Count,
                };
                options.LineItems.Add(sessionLineItem);
            }

            var service = new SessionService();
            Session session = service.Create(options);
            _unitOfWorks.OrderHeader.UpdateStripePaymentID(ShoppingCartVM.OrderHeader.Id, session.Id, session.PaymentIntentId);
            _unitOfWorks.Save();

            Response.Headers.Add("Location", session.Url);
            return new StatusCodeResult(303);
        }

        public async Task<IActionResult> OrderConfirmationAsync(int id)
        {
            OrderHeader orderHeader = _unitOfWorks.OrderHeader.GetById(id);
            var service = new SessionService();
            Session session = service.Get(orderHeader.SessionId);
            //check the stripe status
            if (session.PaymentStatus.ToLower() == "paid")
            {
                _unitOfWorks.OrderHeader.UpdateStatus(id, SD.Status.Approved.ToString(), SD.Status.Approved.ToString());
                _unitOfWorks.Save();
            }

            //remove shopping cart
            List<ShoppingCart> ListCart = _unitOfWorks.ShoppingCart.GetAll(
                includeProperties: "Product",
                criteria: c => c.ApplicationUserID == orderHeader.ApplicationUserId
                ).ToList();
            _unitOfWorks.ShoppingCart.RemoveRange(ListCart);
            _unitOfWorks.Save();

            //clear session value for cart
            HttpContext.Session.Remove(SD.SessionCart);

            //send sms message
            await _SmsSender.SendSMSAsync(orderHeader.PhoneNumber, $"Order Placed on SADA Suits. Your OrderID:{orderHeader.Id}");

            return View(id);
        }

        public IActionResult Plus(int cartId)
        {
            var cartFromDb = _unitOfWorks.ShoppingCart.GetById(cartId);
            _unitOfWorks.ShoppingCart.IncrementCount(cartFromDb, 1);
            _unitOfWorks.Save();

            return RedirectToAction(nameof(Index));
        }
        public IActionResult Minus(int cartId)
        {
            var cartFromDb = _unitOfWorks.ShoppingCart.GetById(cartId);
            if(cartFromDb.Count > 1)
            {
                _unitOfWorks.ShoppingCart.DecrementCount(cartFromDb, 1);
            }
            else //delete
            {
                _unitOfWorks.ShoppingCart.Remove(cartFromDb);
                HttpContext.Session.DecrementValue(SD.SessionCart,1);
            }
            _unitOfWorks.Save();

            return RedirectToAction(nameof(Index));
        }
        public IActionResult Remove(int cartId)
        {
            var cartFromDb = _unitOfWorks.ShoppingCart.GetById(cartId);
            _unitOfWorks.ShoppingCart.Remove(cartFromDb);
            _unitOfWorks.Save();

            HttpContext.Session.DecrementValue(SD.SessionCart, 1);

            return RedirectToAction(nameof(Index));
        }
    }
}