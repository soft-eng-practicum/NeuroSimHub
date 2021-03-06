﻿using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Core.Entities;
using Core.Helper;
using Core.Interfaces;
using Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Web.ViewModels.Account;

namespace Web.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AccountController : ControllerBase
    {
        private readonly JwtSettings _jwtSettings;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signManager;
        private readonly ApplicationDbContext _dbContext;
        private readonly IBlobService _blobService;
        private readonly ILoggerManager _loggerManager;

        public AccountController(IOptions<JwtSettings> jwtSettings, UserManager<ApplicationUser> userManager, 
            SignInManager<ApplicationUser> signManager, ApplicationDbContext dbContext, 
            IBlobService blobService, ILoggerManager loggerManager) 
        {
            _jwtSettings = jwtSettings.Value;
            _userManager = userManager;
            _signManager = signManager;
            _dbContext = dbContext;
            _blobService = blobService;
            _loggerManager = loggerManager;
        }

        #region GET REQUEST
        /*
         * Type : GET
         * URL : /api/account/getuserbyid/
         * Description: Return ApplicationUser from id
         * Response Status: 200 Ok, 404 Not Found
         */
        [HttpGet("[action]/{id}")]
        public IActionResult GetUserByID([FromRoute] int id)
        {
            // Find User
            var user = _dbContext.Users
                .Include(u => u.Followers)
                .Include(u => u.Following)
                .Include(u => u.ProjectUsers)
                .Include(u => u.BlobFiles)
                .SingleOrDefault(x => x.Id == id);
            if (user == null) return NotFound();
            return Ok(new 
            {
                result = user,
                message = "Recieved User: " + user.UserName
            });
        }

        /*
        * Type : GET
        * URL : /api/account/getuserbyname/
        * Description: Return ApplicationUser from username
        * Response Status: 200 Ok, 404 Not Found
        */
        [HttpGet("[action]/{username}")]
        public IActionResult GetUserByName([FromRoute] string username)
        {
            // Find User
            var user = _dbContext.Users
                .Include(u => u.Followers)
                .Include(u => u.Following)
                .Include(u => u.ProjectUsers)
                .Include(u => u.BlobFiles)
                .SingleOrDefault(u => u.UserName == username);
            if (user == null) return NotFound(new { message = "User Not Found" });
            return Ok(new
            {
                result = user,
                message = "Recieved User: " + user.UserName
            });
        }

        /*
        * Type : GET
        * URL : /api/account/getuserrange?
        * Description: Return ApplicationUser(s) from list of id
        * Response Status: 200 Ok, 404 Not Found
        */
        [HttpGet("[action]")]
        public IActionResult GetUserRange([FromQuery(Name = "id")] List<int> ids)
        {
            // Find User
            var users = _dbContext.Users
                .Include(u => u.Followers)
                .Include(u => u.Following)
                .Include(u => u.ProjectUsers)
                .Include(u => u.BlobFiles)
                .Where(u => ids.Contains(u.Id))
                .ToList();
            if (users.Count != ids.Count) return NotFound(new { message = "Contains Invalid User" });

            return Ok(new
            {
                result = users,
                message = "Recieved User Range"
            });
        }

        /*
         * Type : GET
         * URL : /api/account/getuserlist
         * Description: Return all ApplicationUser
         * Response Status: 200 Ok
         */
        [HttpGet("[action]")]
        public IActionResult GetUserList()
        {
            // Query All User Into A List
            var users = _dbContext.Users
                .Include(u => u.Followers)
                .Include(u => u.Following)
                .Include(u => u.ProjectUsers)
                .Include(u => u.BlobFiles)
                .ToList();

            return Ok(new
            {
                result = users,
                message = "Recieved User List"
            });
        }

        /*
         * Type : GET
         * URL : /api/account/search?
         * Description: Return list of matched ApplicationUser from list of searchterms
         * Response Status: 200 Ok, 204 Not Found
         */
        [HttpGet("[action]")]
        public IActionResult Search([FromQuery(Name = "term")] List<string> searchTerms)
        {
            var matchedUser = _dbContext.Users
                .Include(u => u.Followers)
                .Include(u => u.Following)
                .Include(u => u.ProjectUsers)
                .Include(u => u.BlobFiles)
                .ToList()
                .Where(u => searchTerms.All(k => u.UserName.ToLower().Contains(k.ToLower())));
            if (matchedUser.Count() == 0) return NoContent();

            return Ok(new
            {
                result = matchedUser,
                message = "Search Successful"
            });
        }
        #endregion

        #region POST REQUEST
        /*
         * Type : POST
         * URL : /api/account/follow
         * Description: Create and return new UserUser
         * Response Status: 200 Ok, 404 Not Found
         */
        [HttpPost("[action]")]
        public async Task<IActionResult> Follow([FromForm] AccountFollowVM formdata)
        {
            // Find User
            var user = await _dbContext.Users.FindAsync(formdata.UserID);
            if (user == null) return NotFound(new { message = "User Not Found " + formdata.UserID });

            // Find Follower
            var follower = await _dbContext.Users.FindAsync(formdata.FollowerID);
            if (follower == null) return NotFound(new { message = "User Not Found " + formdata.UserID });

            // Create Many To Many Connection
            var userFollower = new UserUser
            {
                UserID = formdata.UserID,
                FollowerID = formdata.FollowerID
            };

            // Add To Database
            await _dbContext.UserUsers.AddAsync(userFollower);

            // Save Change
            await _dbContext.SaveChangesAsync();

            return Ok(new
            {
                result = userFollower,
                message = follower.UserName +  " is now following " + user.UserName
            });
        }

        /*
         * Type : POST
         * URL : /api/account/register
         * Description: Create and return new ApplicationUser
         * Response Status: 200 Ok, 400 Bad Request
         */
        [HttpPost("[action]")]
        public async Task<IActionResult> Register([FromForm] AccountRegisterVM formdata)
        {

            // Hold Error List
            List<string> errorList = new List<string>();

            // Create User Object
            var user = new ApplicationUser
            {
                Email = formdata.EmailAddress,
                UserName = formdata.Username,
                DateCreated = DateTimeOffset.UtcNow,
                LastOnline = DateTimeOffset.UtcNow,
                SecurityStamp = Guid.NewGuid().ToString()
            };

            // Add User To Database
            var result = await _userManager.CreateAsync(user, formdata.Password);

            // If Successfully Created
            if (result.Succeeded)
            {
                // Add Role To User
                await _userManager.AddToRoleAsync(user, "Customer");

                // Return Ok Request
                return Ok(new
                {
                    result = user,
                    message = "Registration Successful"
                });
            }
            else
            {
                // Add Error To ErrorList
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError("", error.Description);
                    errorList.Add(error.Description);
                }
            }

            // Return Bad Request Status With ErrorList
            return BadRequest(new { message = errorList });
        }

        /*
         * Type : POST
         * URL : /api/account/login
         * Param : UserLoginViewModel
         * Description: Login and return Application User, login token, and expiration time
         * Response Status: 200 Ok, 401 Unauthorized
         */
        [HttpPost("[action]")]
        public async Task<IActionResult> Login([FromForm] AccountLoginVM formdata)
        {

            // Get The User
            var user = await _userManager.FindByNameAsync(formdata.Username);

            // Get The User Role
            //var roles = await _userManager.GetRolesAsync(user);

            // Generate Key Token
            var key = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(_jwtSettings.Secret));

            // Generate Expiration Time For Token
            double tokenExpiryTime = Convert.ToDouble(_jwtSettings.ExpireTime);

            // Check Login Status
            if (user != null && await _userManager.CheckPasswordAsync(user, formdata.Password))
            {
                // Create JWT Token Handler
                var tokenHandler = new JwtSecurityTokenHandler();

                // Create Token Descriptor
                var tokenDescriptor = new SecurityTokenDescriptor
                {
                    Subject = new ClaimsIdentity(new Claim[]
                    {
                        new Claim(JwtRegisteredClaimNames.Sub, formdata.Username),
                        new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                        //new Claim(ClaimTypes.Role, roles.FirstOrDefault()),
                        new Claim("LoggedOn", DateTime.Now.ToString())
                    }),

                    SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256Signature),
                    Issuer = _jwtSettings.Issuer,
                    Audience = _jwtSettings.Audience,
                    Expires = DateTime.UtcNow.AddMinutes(tokenExpiryTime)
                };

                // Create Token
                var token = tokenHandler.CreateToken(tokenDescriptor);

                // Update Last Online
                user.LastOnline = DateTime.Now;

                // Save Database Change
                await _dbContext.SaveChangesAsync();

                _dbContext.Entry(user).Collection(u => u.ProjectUsers).Load();
                _dbContext.Entry(user).Collection(u => u.BlobFiles).Load();
                _dbContext.Entry(user).Collection(u => u.Followers).Load();
                _dbContext.Entry(user).Collection(u => u.Following).Load();

                // Return OK Request
                return Ok(new
                {
                    result = user,
                    token = tokenHandler.WriteToken(token),
                    expiration = token.ValidTo,
                    message = "Login Successful"
                });

            }
            else
            {

                ModelState.AddModelError("", "Username/Password was not found");

                // Return Unauthorized Status If Unable To Login
                return Unauthorized(new
                {
                    LoginError = "Please Check the Login Creddentials - Invalid Username/Password was entered"
                });
            }
        }

        /*
         * Type : POST
         * URL : /api/account/uploadprofileimage
         * Description: Upload File To Azure Storage
         */
        [HttpPost("[action]")]
        public async Task<IActionResult> UploadProfileImage([FromForm] AccountUploadVM formdata)
        {
            try
            {
                // Reture Bad Request Status
                if (formdata.File == null) return BadRequest("Null File");
                if (formdata.File.Length == 0) return BadRequest("Empty File");

                // Find User
                var user = await _dbContext.Users.FindAsync(formdata.UserID);
                if (user == null) return NotFound(new { message = "User Not Found" });

                //Create File Path With File
                var filePath = user.UserName + "/profileImage" + Path.GetExtension(formdata.File.FileName);

                BlobClient blobClient = await _blobService.UploadFileBlobResizeAsync(formdata.File, "profile", filePath, 250, 250);
                BlobProperties blobProperties = blobClient.GetProperties();

                // Check For Existing
                var blobFile = _dbContext.BlobFiles.FirstOrDefault(x => x.Uri == blobClient.Uri.AbsoluteUri.ToString());
                if (blobFile != null)
                {
                    blobFile.Extension = Path.GetExtension(formdata.File.FileName);
                    blobFile.Size = (int)blobProperties.ContentLength;
                    blobFile.Uri = blobClient.Uri.AbsoluteUri.ToString();
                    blobFile.LastModified = blobProperties.LastModified.LocalDateTime;

                    // Set Entity State
                    _dbContext.Entry(blobFile).State = EntityState.Modified;

                    await _dbContext.SaveChangesAsync();

                    return Ok(new { result = blobFile, message = "Profile Image Updated" });
                }

                // Create BlobFile
                var newBlobFile = new BlobFile
                {
                    Container = "profile",
                    Directory = user.UserName + "/",
                    Name = "profileImage",
                    Extension = Path.GetExtension(formdata.File.FileName),
                    Size = (int)blobProperties.ContentLength,
                    Uri = blobClient.Uri.AbsoluteUri.ToString(),
                    DateCreated = blobProperties.CreatedOn.UtcDateTime,
                    LastModified = blobProperties.LastModified.UtcDateTime,
                    UserID = formdata.UserID
                };

                // Update Database with entry
                await _dbContext.BlobFiles.AddAsync(newBlobFile);
                await _dbContext.SaveChangesAsync();

                // Return Ok Status
                return Ok(new
                {
                    result = newBlobFile,
                    message = "File Successfully Uploaded"
                });
            }
            catch (Exception e)
            {
                // Return Bad Request If There Is Any Error
                return BadRequest(new
                {
                    error = e
                });
            }
        }
        #endregion

        #region PUT REQUEST
        /*
        * Type : PUT
        * URL : /api/account/updateuser/
        * Param : {userID}, ProjectViewModel
        * Description: Update Project
        * Response Status: 200 Ok, 404 Not Found
        */
        [HttpPut("[action]/{userID}")]
        public IActionResult UpdateUser([FromRoute] int userID, [FromForm] AccountUpdateVM formdata)
        {
            // Check Model State
            if (!ModelState.IsValid) return BadRequest(ModelState);

            // Find User
            var user = _dbContext.Users
                .Include(u => u.Followers)
                .Include(u => u.Following)
                .Include(u => u.ProjectUsers)
                .Include(u => u.BlobFiles)
                .FirstOrDefault(u => u.Id == userID);
            if (user == null) return NotFound(new { message = "User Not Found" });

            // Update Bio
            user.Bio = formdata.Bio;

            // Save Change
            _dbContext.SaveChanges();

            // Return Ok Status
            return Ok(new
            {
                result = user,
                message = "User has been updated"
            });

        }
        #endregion

        #region DELETE REQUEST
        /*
         * Type : DELETE
         * URL : /api/account/unfollow/
         * Param : {userID}/{followerID}
         * Description: Have the follower unfollow the user
         * Response Status: 200 Ok, 404 Not Found
         */
        [HttpDelete("[action]/{userID}/{followerID}")]
        public async Task<IActionResult> Unfollow([FromRoute] int userID, [FromRoute] int followerID)
        {
            // Find User
            var user = await _dbContext.Users.FindAsync(userID);
            if (user == null) return NotFound(new { message = "User Not Found" });

            // Find Follower
            var follower = await _dbContext.Users.FindAsync(followerID);
            if (follower == null) return NotFound(new { message = "Follower Not Found" });

            // Find Many To Many
            var userFollower = await _dbContext.UserUsers.FindAsync(user.Id, follower.Id);
            if (userFollower == null) return NotFound(new { message = "User Follower Connection Not FOund" });

            // Remove Project
            _dbContext.UserUsers.Remove(userFollower);

            // Save Change
            await _dbContext.SaveChangesAsync();

            return Ok(new
            {
                result = userFollower,
                message = follower.UserName + " has unfollow " + user.UserName
            });
        }
        #endregion

        #region Extra
        /*
         * Type : GET
         * URL : /api/account/getprojects/
         * Param : {userID}
         * Description: Get list of project user has connection to
         * Response Status: 200 Ok, 204 No Content
         */
        [HttpGet("[action]/{userID}")]
        public IActionResult GetProjects([FromRoute] int userID)
        {
            // Find User
            //var user = _dbContext.Users.SingleOrDefault(x => x.Id == userID);
            //if (user == null) return NotFound(new { message = "User Not Found" });

            var userProjects = _dbContext.ProjectUsers
                .Include(pu => pu.Project)
                    .ThenInclude(p => p.BlobFiles)
                .Include(pu => pu.Project)
                    .ThenInclude(p => p.ProjectUsers)
                .Include(pu => pu.Project)
                    .ThenInclude(p => p.ProjectTags)
                    .ThenInclude(pt => pt.Tag)
                .Where(pu => pu.UserID == userID)
                .AsEnumerable();

                
            //if (userProjects.Count() == 0) return NoContent();

            // Return Ok Status
            return Ok(new
            {
                result = userProjects,
                message = "Recieved User Project"
            });
        }

        /*
        * Type : GET
        * URL : /api/account/getfollowers/
        * Param : {userID}
        * Description: Get follower from user id
        * Response Status: 200 Ok, 204 No Content, 404 Not Found
        */
        [HttpGet("[action]/{userID}")]
        public IActionResult GetFollowers([FromRoute] int userID)
        {
            // Find User
            var user = _dbContext.Users.SingleOrDefault(x => x.Id == userID);
            if (user == null) return NotFound(new { message = "User Not Found" });

            var userFollowers = _dbContext.UserUsers
                .Include(uu => uu.Follower).ThenInclude(f => f.Followers)
                .Include(uu => uu.Follower).ThenInclude(f => f.Following)
                .Where(u => u.UserID == userID)
                .ToList();
                
            if (userFollowers.Count() == 0) return NoContent();

            return Ok(new
            {
                result = userFollowers,
                message = "Recieved User Follower"
            });
        }

        /*
        * Type : GET
        * URL : /api/account/getfollowings/
        * Param : {userID}
        * Description: Get user following from user id
        * Response Status: 200 Ok, 204 No Content, 404 Not Found
        */
        [HttpGet("[action]/{userID}")]
        public IActionResult GetFollowings([FromRoute] int userID)
        {
            // Find User
            var user = _dbContext.Users
                .Include(u => u.Following)
                .Include(u => u.Followers)
                .SingleOrDefault(x => x.Id == userID);
            if (user == null) return NotFound(new { message = "User Not Found" });

            var userFollowings = _dbContext.UserUsers
                .Include(uu => uu.User).ThenInclude(f => f.Followers)
                .Include(uu => uu.User).ThenInclude(f => f.Following)
                .Where(u => u.FollowerID == userID)
                .ToList();
            if (userFollowings.Count() == 0) return NoContent();

            return Ok(new
            {
                result = userFollowings,
                message = "Recieved User Following"
            });
        }

        /*
         * Type : DELETE
         * URL : /api/account/deleteprofileimage/
         * Param : {fileID}
         * Description: Delete File From Azure Storage
         */
        [HttpDelete("[action]/{fileID}")]
        public async Task<IActionResult> DeleteProfileImage([FromRoute] int fileID)
        {
            try
            {
                // Find File
                var blobFile = await _dbContext.BlobFiles.FindAsync(fileID);
                if (blobFile == null) return NotFound(new { message = "File Not Found" });

                await _blobService.DeleteBlobAsync(blobFile);

                // Delete Blob Files From Database
                _dbContext.BlobFiles.Remove(blobFile);

                // Save Change to Database
                await _dbContext.SaveChangesAsync();

                // Return Ok Status
                return Ok(new
                {
                    result = blobFile,
                    message = "File Successfully Deleted"
                });
            }
            catch (Exception e)
            {
                // Return Bad Request If There Is Any Error
                return BadRequest(new
                {
                    error = e
                });
            }

        }
        #endregion
    }

}
