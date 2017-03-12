﻿using Lms.Domain.Gateways.Payments;
using Lms.Domain.Models.Courses;
using Lms.Domain.Models.Exceptions;
using Lms.Domain.Models.Users;
using Lms.Domain.Repositories;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Lms.Domain.Services.Courses
{
    public class SessionServiceImpl : ISessionService
    {
        protected IUnitOfWork unitOfWork;
        protected IPaymentService paymentService;
        static Logger logger = LogManager.GetCurrentClassLogger();

        public SessionServiceImpl(IUnitOfWork unitOfWork, IPaymentService paymentService)
        {
            this.unitOfWork = unitOfWork;
            this.paymentService = paymentService;
        }

        public void ChargeSession(string userId, string sessionId, string cardNumber, string expireYear, string expireMonth, string cvv2)
        {
            var session = unitOfWork.SessionRepository.GetById(sessionId);
            if (session.Cost != null && session.Cost > 0)
            {
                try
                {
                    paymentService.Charge((int)(session.Cost.Value * 100), "usd", "", cardNumber, expireYear, expireMonth, cvv2);

                    EnrollUser(userId, sessionId);
                    
                }
                catch (PaymentException pex)
                {
                    logger.Error(pex.ToString());
                    throw pex;
                }

            }
        }

        public void EnrollUser(string userId, string sessionId)
        {
            var lessons = unitOfWork.SessionRepository.GetById(sessionId).Course.Lessons.Where(x => !x.IsDeleted).ToList();
            var enrollment = new Enrollment(userId, sessionId, lessons);
            unitOfWork.EnrollmentRepository.Insert(enrollment);
            unitOfWork.SaveChanges();
        }


        public void WithdrawEnrollment(string enrollmentId, string userId)
        {
            var enroll = unitOfWork.EnrollmentRepository.GetById(enrollmentId);

            if (enroll.UserId == userId)
            {
                enroll.Withdraw();
                unitOfWork.EnrollmentRepository.Update(enroll);
                unitOfWork.SaveChanges();
            }
        }

        public IEnumerable<Session> LoadNewSessions(string companyId, string userId)
        {
            var courses = unitOfWork.CourseRepository.FindAsNoTracking(x => x.CompanyId == companyId && !x.IsDeleted && x.IsPublished);
            var user = unitOfWork.UserRepository.GetById(userId);

            if (user.UserType == Models.Users.UserTypeEnum.External)
            {
                courses = courses.Where(x => x.CourseAccess == CourseAccessEnum.ExtenralUsersOnly || x.CourseAccess == CourseAccessEnum.BothUsers);
            }
            else // internal
            {
                courses = courses.Where(x => x.CourseAccess == CourseAccessEnum.InternalUsersOnly || x.CourseAccess == CourseAccessEnum.BothUsers);
            }

            var utcNow = DateTime.UtcNow;
            return courses.SelectMany(x => x.Sessions).Where(x => !x.IsDeleted && x.EnrollEnd >= utcNow).OrderByDescending(x => x.SessionStart).Take(6);
        }



        public Session GetSessionById(string companyId, string sessionId)
        {
            var session = unitOfWork.CourseRepository.GetAll().SelectMany(x => x.Sessions).SingleOrDefault(x => x.Id == sessionId);

            if (session != null && session.Course.CompanyId == companyId)
            {
                return session;
            }

            return null;

        }



        public void CreateSession(string companyId, string courseId, string sessionName, string description, string cost,
            string sessionStartDate, string sessionEndDate, string enrollmentStartDate, string enrollmentEndDate)
        {
            Course course = unitOfWork.CourseRepository.GetById(courseId);
            if (course.CompanyId == companyId)
            {
                var sessionStart = DateTime.Parse(sessionStartDate);
                var sessionEnd = DateTime.Parse(sessionEndDate);
                var enrollmentStart = DateTime.Parse(enrollmentStartDate);
                var enrollmentEnd = DateTime.Parse(enrollmentEndDate);
                double? doubleCost = null;
                if (!string.IsNullOrEmpty(cost))
                    doubleCost = double.Parse(cost);

                course.AddSession(sessionName, description, doubleCost, sessionStart, sessionEnd, enrollmentStart, enrollmentEnd);
                unitOfWork.CourseRepository.Update(course);
                unitOfWork.SaveChanges();
            }
        }

        public void DeleteSession(string companyId, string updaterId, string courseId, string sessionId)
        {
            logger.Info("[user: {0}] deletes a session [courseId: {1}, sessionId: {2}]", updaterId, courseId, sessionId);
            var course = unitOfWork.CourseRepository.GetById(courseId);
            if (course.Company.Id == companyId)
            {
                var session = course.Sessions.SingleOrDefault(x => x.Id == sessionId);
                if (session != null)
                {
                    session.IsDeleted = true;
                    session.UpdatedTs = DateTime.UtcNow;

                    unitOfWork.CourseRepository.Update(course);
                    unitOfWork.SaveChanges();
                }
            }
        }



    }
}